using LlamitasStore.Data;
using LlamitasStore.Data.Semilla;
using LlamitasStore.Models.Entities;
using LlamitasStore.Services.Admin;
using LlamitasStore.Services.Ventas;
using LlamitasStore.Tests.Soporte;
using LlamitasStore.Tests.Ventas;
using LlamitasStore.ViewModels;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LlamitasStore.Tests.Admin;

public sealed class ServiciosAdminTests : IAsyncLifetime
{
    private EscenarioVentas _e = null!;
    public async Task InitializeAsync() => _e = await EscenarioVentas.CrearAsync();
    public async Task DisposeAsync() => await _e.DisposeAsync();

    private async Task<int> StockAsync(int id)
    {
        await using var db = _e.Bd.NuevoContexto();
        return await db.Productos.Where(p => p.Id == id).Select(p => p.Stock).SingleAsync();
    }

    private ServicioInventario Inventario(AppDbContext db) => new(db, new RelojFijo(EscenarioVentas.Hoy));

    // ---------- Inventario ----------

    [Fact]
    public async Task Entrada_salida_y_conteo_registran_movimientos_y_nunca_dejan_stock_negativo()
    {
        await using var db = _e.Bd.NuevoContexto();
        var s = Inventario(db);

        Assert.Null(await s.AjustarAsync(_e.A, TipoMovimiento.Entrada, 10, "Compra local", _e.Ana)); // 5 → 15
        Assert.Null(await s.AjustarAsync(_e.A, TipoMovimiento.Salida, 3, "Rotura", _e.Ana));        // → 12
        Assert.Contains("No hay stock suficiente", await s.AjustarAsync(_e.A, TipoMovimiento.Salida, 50, "Error", _e.Ana));
        Assert.Null(await s.AjustarAsync(_e.A, TipoMovimiento.Ajuste, 9, "Inventario físico", _e.Ana)); // → 9
        Assert.NotNull(await s.AjustarAsync(_e.A, TipoMovimiento.Entrada, 1, "  ", _e.Ana));            // sin motivo

        Assert.Equal(9, await StockAsync(_e.A));
        await using var ctx = _e.Bd.NuevoContexto();
        var movs = await ctx.MovimientosInventario.Where(m => m.ProductoId == _e.A).OrderBy(m => m.Id).ToListAsync();
        Assert.Equal([10, -3, -3], movs.Select(m => m.Cantidad).ToArray());
        Assert.Equal([15, 12, 9], movs.Select(m => m.StockResultante).ToArray());
    }

    [Fact]
    public async Task Orden_de_compra_suma_stock_solo_al_recibirse_y_una_sola_vez()
    {
        await using var db = _e.Bd.NuevoContexto();
        db.Proveedores.Add(new Proveedor { Nombre = "Mayorista" });
        await db.SaveChangesAsync();
        var prov = await db.Proveedores.Select(p => p.Id).SingleAsync();
        var s = Inventario(db);

        var (orden, error) = await s.CrearOrdenCompraAsync(prov, [new(_e.A, 4, 700), new(_e.B, 2, 150)], "Pedido semanal", _e.Ana);
        Assert.Null(error);
        Assert.Matches(@"^OC-20261126-\d{4}$", orden!.Numero);
        Assert.Equal(3100m, orden.Total);
        Assert.Equal(5, await StockAsync(_e.A)); // borrador: todavía no suma

        Assert.Null(await s.RecibirOrdenCompraAsync(orden.Id, _e.Ana));
        Assert.Equal(9, await StockAsync(_e.A));
        Assert.Equal(3, await StockAsync(_e.B));

        Assert.NotNull(await s.RecibirOrdenCompraAsync(orden.Id, _e.Ana)); // no se recibe dos veces
        Assert.NotNull(await s.CancelarOrdenCompraAsync(orden.Id));        // ni se cancela una recibida
        Assert.Equal(9, await StockAsync(_e.A));
    }

    [Fact]
    public async Task Orden_de_compra_rechaza_lineas_vacias_y_productos_repetidos()
    {
        await using var db = _e.Bd.NuevoContexto();
        db.Proveedores.Add(new Proveedor { Nombre = "P" });
        await db.SaveChangesAsync();
        var prov = await db.Proveedores.Select(p => p.Id).SingleAsync();
        var s = Inventario(db);

        Assert.NotNull((await s.CrearOrdenCompraAsync(prov, [new(0, 0, 0)], null, _e.Ana)).Error);
        Assert.Contains("repetidos", (await s.CrearOrdenCompraAsync(prov, [new(_e.A, 1, 1), new(_e.A, 2, 1)], null, _e.Ana)).Error);
    }

    // ---------- Pedidos ----------

    private async Task<string> PedidoAsync(MetodoPago metodo = MetodoPago.ContraEntrega)
    {
        var (db, carrito, pedidos, _) = _e.Servicios();
        await using (db)
        {
            await carrito.AgregarAsync(new DuenoCarrito(_e.Ana, null), _e.A, 2);
            return (await pedidos.CrearDesdeCarritoAsync(_e.Ana, new CheckoutVm { DireccionId = _e.DireccionDe(_e.Ana), MetodoPago = metodo })).Numero!;
        }
    }

    private ServicioAdminPedidos AdminPedidos(AppDbContext db)
    {
        var (_, _, pedidos, _) = _e.Servicios();
        var reloj = new RelojFijo(EscenarioVentas.Hoy);
        var cupones = new ServicioCupones(db, reloj);
        var ajustes = new LlamitasStore.Services.AjustesTienda(db, new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()));
        var servicioPedidos = new ServicioPedidos(db, new LlamitasStore.Services.Catalogo.ServicioPrecios(db, reloj), cupones, ajustes, reloj,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ServicioPedidos>.Instance);
        return new ServicioAdminPedidos(db, servicioPedidos, reloj);
    }

    [Fact]
    public async Task Flujo_de_estados_valido_y_contra_entrega_se_cobra_al_entregar()
    {
        var numero = await PedidoAsync();
        await using var db = _e.Bd.NuevoContexto();
        var s = AdminPedidos(db);

        Assert.Contains("No se puede pasar", await s.CambiarEstadoAsync(numero, EstadoPedido.Enviado, null, _e.Beto)); // salto inválido
        foreach (var e in new[] { EstadoPedido.Confirmado, EstadoPedido.EnPreparacion, EstadoPedido.Enviado, EstadoPedido.Entregado })
            Assert.Null(await s.CambiarEstadoAsync(numero, e, null, _e.Beto));
        Assert.NotNull(await s.CambiarEstadoAsync(numero, EstadoPedido.Cancelado, null, _e.Beto)); // entregado es final

        await using var ctx = _e.Bd.NuevoContexto();
        var p = await ctx.Pedidos.Include(x => x.Pagos).Include(x => x.Historial).SingleAsync();
        Assert.Equal(EstadoPedido.Entregado, p.Estado);
        Assert.Equal(EstadoPago.Confirmado, p.Pagos.Single().Estado);
        Assert.Equal(5, p.Historial.Count);
        Assert.Equal(5, await ctx.Notificaciones.CountAsync(n => n.UsuarioId == _e.Ana)); // creación + 4 cambios
    }

    [Fact]
    public async Task Transferencia_no_se_confirma_sin_pago_verificado()
    {
        var numero = await PedidoAsync(MetodoPago.Transferencia);
        await using var db = _e.Bd.NuevoContexto();
        var s = AdminPedidos(db);

        Assert.Contains("confirma el pago", await s.CambiarEstadoAsync(numero, EstadoPedido.Confirmado, null, _e.Beto));
        Assert.Null(await s.ConfirmarPagoAsync(numero, "TRX-9", _e.Beto));
        Assert.Null(await s.CambiarEstadoAsync(numero, EstadoPedido.Confirmado, null, _e.Beto));
    }

    [Fact]
    public async Task Cancelar_desde_el_panel_devuelve_stock_y_reembolsa_pagos_confirmados()
    {
        var numero = await PedidoAsync(MetodoPago.Transferencia);
        Assert.Equal(3, await StockAsync(_e.A));
        await using (var db = _e.Bd.NuevoContexto())
        {
            var s = AdminPedidos(db);
            Assert.Null(await s.ConfirmarPagoAsync(numero, null, _e.Beto));
            Assert.Null(await s.CambiarEstadoAsync(numero, EstadoPedido.Cancelado, "Sin stock en depósito", _e.Beto));
        }
        Assert.Equal(5, await StockAsync(_e.A));
        await using var ctx = _e.Bd.NuevoContexto();
        Assert.Equal(EstadoPago.Reembolsado, (await ctx.Pagos.SingleAsync()).Estado);
    }

    // ---------- Imágenes ----------

    [Theory]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0, 0, 0, 0, 0 }, ".jpg")]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0 }, ".png")]
    [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46, 1, 2, 3, 4, 0x57, 0x45, 0x42, 0x50 }, ".webp")]
    [InlineData(new byte[] { 0x3C, 0x73, 0x76, 0x67, 0x20, 0, 0, 0, 0, 0, 0, 0 }, null)]          // "<svg " (puede llevar scripts)
    [InlineData(new byte[] { 0x3C, 0x3F, 0x70, 0x68, 0x70, 0, 0, 0, 0, 0, 0, 0 }, null)]          // "<?php" disfrazado de .jpg
    [InlineData(new byte[] { 0x4D, 0x5A, 0x90, 0x00, 0, 0, 0, 0, 0, 0, 0, 0 }, null)]             // ejecutable de Windows
    public void Imagenes_se_validan_por_su_contenido_real(byte[] cabecera, string? esperado) =>
        Assert.Equal(esperado, ServicioAdminProductos.DetectarFormato(cabecera));

    // ---------- Usuarios y roles ----------

    private async Task<ServicioAdminUsuarios> UsuariosAsync(IServiceScope scope)
    {
        var sp = scope.ServiceProvider;
        await new DatosIniciales(sp.GetRequiredService<AppDbContext>(), sp.GetRequiredService<RoleManager<Rol>>(), sp.GetRequiredService<UserManager<Usuario>>(),
            sp.GetRequiredService<IConfiguration>(), sp.GetRequiredService<ILogger<DatosIniciales>>()).EjecutarAsync();
        var um = sp.GetRequiredService<UserManager<Usuario>>();
        await um.AddToRoleAsync((await um.FindByIdAsync(_e.Ana.ToString()))!, Roles.Administrador);
        await um.AddToRoleAsync((await um.FindByIdAsync(_e.Beto.ToString()))!, Roles.Cliente);
        return new ServicioAdminUsuarios(sp.GetRequiredService<AppDbContext>(), um, sp.GetRequiredService<RoleManager<Rol>>());
    }

    [Fact]
    public async Task Nadie_se_cambia_a_si_mismo_y_el_ultimo_admin_esta_protegido()
    {
        using var scope = _e.Bd.Servicios.CreateScope();
        var s = await UsuariosAsync(scope);

        Assert.Contains("propio rol", await s.CambiarRolAsync(_e.Ana, Roles.Cliente, actorId: _e.Ana));
        Assert.Contains("propia cuenta", await s.CambiarEstadoAsync(_e.Ana, EstadoUsuario.Inactivo, actorId: _e.Ana));
        Assert.Contains("último administrador", await s.CambiarRolAsync(_e.Ana, Roles.Cliente, actorId: _e.Beto));
        Assert.Contains("último administrador", await s.CambiarEstadoAsync(_e.Ana, EstadoUsuario.Suspendido, actorId: _e.Beto));

        // Con un segundo admin, ya se puede.
        Assert.Null(await s.CambiarRolAsync(_e.Beto, Roles.Administrador, actorId: _e.Ana));
        Assert.Null(await s.CambiarRolAsync(_e.Ana, Roles.Vendedor, actorId: _e.Beto));
    }

    [Fact]
    public async Task Matriz_de_permisos_crear_implica_leer_y_el_administrador_es_fijo()
    {
        using var scope = _e.Bd.Servicios.CreateScope();
        var s = await UsuariosAsync(scope);
        Assert.Null(await s.CrearRolAsync("Bodega", "Solo inventario"));
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var bodega = await db.Roles.SingleAsync(r => r.Name == "Bodega");
        var modulo = await db.Modulos.SingleAsync(m => m.Clave == Modulos.Inventario);

        Assert.Null(await s.GuardarMatrizAsync(bodega.Id, [new FilaPermiso(modulo.Id, "", "", Leer: false, Crear: true, Editar: false, Eliminar: false)]));
        var fila = (await s.MatrizAsync(bodega.Id)).Single(f => f.ModuloId == modulo.Id);
        Assert.True(fila.Leer && fila.Crear && !fila.Editar);

        var admin = await db.Roles.SingleAsync(r => r.Name == Roles.Administrador);
        Assert.NotNull(await s.GuardarMatrizAsync(admin.Id, []));
        Assert.Contains("sistema", await s.EliminarRolAsync(admin.Id));
        Assert.Null(await s.EliminarRolAsync(bodega.Id));
    }
}
