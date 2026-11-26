using LlamitasStore.Models.Entities;
using LlamitasStore.Services.Ventas;
using LlamitasStore.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace LlamitasStore.Tests.Ventas;

/// <summary>Creación de pedidos: inventario, precios congelados, cupones, atomicidad y cancelación.</summary>
public sealed class PedidosTests : IAsyncLifetime
{
    private EscenarioVentas _e = null!;
    public async Task InitializeAsync() => _e = await EscenarioVentas.CrearAsync();
    public async Task DisposeAsync() => await _e.DisposeAsync();

    private CheckoutVm Checkout(int usuario, MetodoPago metodo = MetodoPago.ContraEntrega) =>
        new() { DireccionId = _e.DireccionDe(usuario), MetodoPago = metodo };

    private async Task AgregarAsync(int usuario, params (int Producto, int Cantidad)[] items)
    {
        var (db, carrito, _, _) = _e.Servicios();
        await using (db) foreach (var (p, c) in items) await carrito.AgregarAsync(new DuenoCarrito(usuario, null), p, c);
    }

    private async Task<int> StockAsync(int productoId)
    {
        await using var db = _e.Bd.NuevoContexto();
        return await db.Productos.Where(p => p.Id == productoId).Select(p => p.Stock).SingleAsync();
    }

    [Fact]
    public async Task Pedido_descuenta_stock_registra_movimientos_pago_e_historial_y_vacia_el_carrito()
    {
        await AgregarAsync(_e.Ana, (_e.A, 2), (_e.B, 1));
        var (db, carrito, pedidos, _) = _e.Servicios();
        await using (db)
        {
            var r = await pedidos.CrearDesdeCarritoAsync(_e.Ana, Checkout(_e.Ana));

            Assert.True(r.Exito, string.Join(" ", r.Errores));
            Assert.Matches(@"^LS-20261126-\d{5}$", r.Numero);
            Assert.Equal(0, await carrito.ContarAsync(new DuenoCarrito(_e.Ana, null)));
        }

        Assert.Equal(3, await StockAsync(_e.A));
        Assert.Equal(0, await StockAsync(_e.B));

        await using var ctx = _e.Bd.NuevoContexto();
        var pedido = await ctx.Pedidos.Include(p => p.Detalles).Include(p => p.Pagos).Include(p => p.Historial).SingleAsync();
        Assert.Equal((2200m, 0m, 0m, 2200m), (pedido.Subtotal, pedido.Descuento, pedido.CostoEnvio, pedido.Total)); // ≥ 1500: envío gratis
        Assert.Equal(EstadoPedido.Pendiente, pedido.Estado);
        Assert.Equal("Calle 1", pedido.EnvioDireccion);
        Assert.Equal(EstadoPago.Pendiente, Assert.Single(pedido.Pagos).Estado);
        Assert.Single(pedido.Historial);

        var movimientos = await ctx.MovimientosInventario.Where(m => m.PedidoId == pedido.Id).ToListAsync();
        Assert.Equal(2, movimientos.Count);
        Assert.All(movimientos, m => Assert.Equal(TipoMovimiento.Salida, m.Tipo));
        Assert.Equal(3, movimientos.Single(m => m.ProductoId == _e.A).StockResultante);
        Assert.Equal(2, (await ctx.Productos.FindAsync(_e.A))!.TotalVendidos);
    }

    [Fact]
    public async Task Los_precios_del_pedido_quedan_congelados_aunque_luego_cambie_el_precio()
    {
        await AgregarAsync(_e.Ana, (_e.A, 1));
        var (db, _, pedidos, _) = _e.Servicios();
        await using (db) await pedidos.CrearDesdeCarritoAsync(_e.Ana, Checkout(_e.Ana));

        await using var ctx = _e.Bd.NuevoContexto();
        (await ctx.Productos.FindAsync(_e.A))!.Precio = 9999;
        await ctx.SaveChangesAsync();

        Assert.Equal(1000m, (await ctx.DetallesPedido.SingleAsync()).PrecioUnitario);
    }

    [Fact]
    public async Task La_ultima_unidad_solo_la_compra_uno_y_el_otro_pedido_no_deja_rastro()
    {
        // Ana y Beto tienen en el carrito la única unidad de B.
        await AgregarAsync(_e.Ana, (_e.B, 1));
        await AgregarAsync(_e.Beto, (_e.A, 1), (_e.B, 1));

        var (db1, _, p1, _) = _e.Servicios();
        await using (db1) Assert.True((await p1.CrearDesdeCarritoAsync(_e.Ana, Checkout(_e.Ana))).Exito);

        var (db2, carrito2, p2, _) = _e.Servicios();
        await using (db2)
        {
            var r = await p2.CrearDesdeCarritoAsync(_e.Beto, Checkout(_e.Beto));
            Assert.False(r.Exito);
            Assert.Contains(r.Errores, e => e.Contains("se agotó"));
            Assert.Equal(2, await carrito2.ContarAsync(new DuenoCarrito(_e.Beto, null))); // el carrito de Beto sigue intacto
        }

        // Rollback completo: el producto A de Beto NO se descontó.
        Assert.Equal(5, await StockAsync(_e.A));
        Assert.Equal(0, await StockAsync(_e.B));
        await using var ctx = _e.Bd.NuevoContexto();
        Assert.Equal(1, await ctx.Pedidos.CountAsync());
    }

    [Fact]
    public async Task Cupon_se_aplica_se_cuenta_y_respeta_el_limite_por_usuario()
    {
        await AgregarAsync(_e.Ana, (_e.A, 1));
        var (db, carrito, pedidos, _) = _e.Servicios();
        await using (db)
        {
            await carrito.AplicarCuponAsync(new DuenoCarrito(_e.Ana, null), "P10");
            var r = await pedidos.CrearDesdeCarritoAsync(_e.Ana, Checkout(_e.Ana));
            Assert.True(r.Exito);
        }

        await using (var ctx = _e.Bd.NuevoContexto())
        {
            var pedido = await ctx.Pedidos.SingleAsync();
            Assert.Equal((100m, 25m, 925m, "P10"), (pedido.Descuento, pedido.CostoEnvio, pedido.Total, pedido.CodigoCupon));
            Assert.Equal(1, (await ctx.Cupones.SingleAsync(c => c.Codigo == "P10")).UsosActuales);
        }

        // Segundo uso del mismo usuario: rechazado.
        await AgregarAsync(_e.Ana, (_e.A, 1));
        var (db2, carrito2, _, _) = _e.Servicios();
        await using (db2)
        {
            var v = await carrito2.AplicarCuponAsync(new DuenoCarrito(_e.Ana, null), "P10");
            Assert.False(v.Valido);
            Assert.Contains("máximo de veces", v.Error);
        }
    }

    [Fact]
    public async Task Cupon_de_un_solo_uso_no_se_puede_usar_dos_veces_aunque_estuviera_aplicado()
    {
        await AgregarAsync(_e.Ana, (_e.A, 1));
        await AgregarAsync(_e.Beto, (_e.A, 1));
        var (db, carrito, _, _) = _e.Servicios();
        await using (db)
        {
            Assert.True((await carrito.AplicarCuponAsync(new DuenoCarrito(_e.Ana, null), "F50")).Valido);
            Assert.True((await carrito.AplicarCuponAsync(new DuenoCarrito(_e.Beto, null), "F50")).Valido);
        }

        var (db1, _, p1, _) = _e.Servicios();
        await using (db1) Assert.True((await p1.CrearDesdeCarritoAsync(_e.Ana, Checkout(_e.Ana))).Exito);

        var (db2, _, p2, _) = _e.Servicios();
        await using (db2)
        {
            var r = await p2.CrearDesdeCarritoAsync(_e.Beto, Checkout(_e.Beto));
            Assert.False(r.Exito);
            Assert.Contains(r.Errores, e => e.Contains("F50"));
        }
        Assert.Equal(4, await StockAsync(_e.A)); // solo se descontó la unidad de Ana
    }

    [Fact]
    public async Task No_se_puede_usar_la_direccion_de_otro_usuario()
    {
        await AgregarAsync(_e.Ana, (_e.A, 1));
        var (db, _, pedidos, _) = _e.Servicios();
        await using (db)
        {
            var r = await pedidos.CrearDesdeCarritoAsync(_e.Ana, new CheckoutVm { DireccionId = _e.DireccionDe(_e.Beto), MetodoPago = MetodoPago.ContraEntrega });
            Assert.False(r.Exito);
        }
        Assert.Equal(5, await StockAsync(_e.A));
    }

    [Fact]
    public async Task Cancelar_un_pedido_pendiente_devuelve_stock_y_libera_el_cupon()
    {
        await AgregarAsync(_e.Ana, (_e.A, 3));
        string numero;
        var (db, carrito, pedidos, _) = _e.Servicios();
        await using (db)
        {
            await carrito.AplicarCuponAsync(new DuenoCarrito(_e.Ana, null), "F50");
            numero = (await pedidos.CrearDesdeCarritoAsync(_e.Ana, Checkout(_e.Ana))).Numero!;
        }
        Assert.Equal(2, await StockAsync(_e.A));

        var (db2, _, pedidos2, _) = _e.Servicios();
        await using (db2)
        {
            Assert.False(await pedidos2.CancelarPorClienteAsync(_e.Beto, numero)); // no es suyo
            Assert.True(await pedidos2.CancelarPorClienteAsync(_e.Ana, numero));
            Assert.False(await pedidos2.CancelarPorClienteAsync(_e.Ana, numero)); // ya cancelado
        }

        Assert.Equal(5, await StockAsync(_e.A));
        await using var ctx = _e.Bd.NuevoContexto();
        var pedido = await ctx.Pedidos.Include(p => p.Pagos).SingleAsync();
        Assert.Equal(EstadoPedido.Cancelado, pedido.Estado);
        Assert.Equal(EstadoPago.Rechazado, pedido.Pagos.Single().Estado);
        Assert.Equal(0, (await ctx.Cupones.SingleAsync(c => c.Codigo == "F50")).UsosActuales);
        Assert.Equal(0, (await ctx.Productos.FindAsync(_e.A))!.TotalVendidos);
        Assert.Equal(1, await ctx.MovimientosInventario.CountAsync(m => m.Tipo == TipoMovimiento.Devolucion));
    }

    [Fact]
    public async Task Pedidos_de_otro_usuario_no_se_pueden_consultar()
    {
        await AgregarAsync(_e.Ana, (_e.A, 1));
        var (db, _, pedidos, _) = _e.Servicios();
        await using (db)
        {
            var numero = (await pedidos.CrearDesdeCarritoAsync(_e.Ana, Checkout(_e.Ana))).Numero!;
            Assert.NotNull(await pedidos.ObtenerAsync(_e.Ana, numero));
            Assert.Null(await pedidos.ObtenerAsync(_e.Beto, numero));
            Assert.Empty(await pedidos.ListarAsync(_e.Beto));
        }
    }
}
