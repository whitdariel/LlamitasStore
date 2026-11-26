using System.Net;
using LlamitasStore.Data;
using LlamitasStore.Models.Entities;
using LlamitasStore.Tests.Soporte;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LlamitasStore.Tests.Admin;

/// <summary>Permisos del panel verificados en el servidor, por HTTP, con usuarios reales de cada rol.</summary>
public sealed class PanelHttpTests : IAsyncLifetime
{
    private readonly FabricaApp _app = new();
    private const string Clave = "Llama1234";

    public async Task InitializeAsync()
    {
        await _app.InicializarAsync(async db =>
        {
            var cat = new Categoria { Nombre = "Ratones", Slug = "ratones" };
            var mar = new Marca { Nombre = "Logi", Slug = "logi" };
            db.Productos.Add(new Producto { Sku = "P-1", Nombre = "Ratón", Slug = "raton", DescripcionCorta = "c", Descripcion = "d", Categoria = cat, Marca = mar, Precio = 100, PrecioCosto = 50, Stock = 5 });
            await db.SaveChangesAsync();
        });
        await CrearUsuarioAsync("admin@t.bo", Roles.Administrador);
        await CrearUsuarioAsync("vende@t.bo", Roles.Vendedor);
        await CrearUsuarioAsync("cliente@t.bo", Roles.Cliente);
    }

    public Task DisposeAsync() { _app.Dispose(); return Task.CompletedTask; }

    private async Task CrearUsuarioAsync(string email, string rol)
    {
        using var scope = _app.Services.CreateScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<Usuario>>();
        var u = new Usuario { UserName = email, Email = email, Nombre = rol, Apellido = "Prueba" };
        Assert.True((await um.CreateAsync(u, Clave)).Succeeded);
        await um.AddToRoleAsync(u, rol);
    }

    private async Task<Navegador> ComoAsync(string email)
    {
        var nav = _app.NuevoNavegador();
        Assert.Equal(HttpStatusCode.Redirect, (await nav.IngresarAsync(email, Clave)).StatusCode);
        return nav;
    }

    private static readonly string[] PaginasPanel =
    [
        "/admin", "/admin/pedidos", "/admin/productos", "/admin/productos/nuevo", "/admin/inventario", "/admin/inventario/movimientos",
        "/admin/inventario/precios", "/admin/categorias", "/admin/categorias/nueva", "/admin/marcas", "/admin/marcas/nueva",
        "/admin/proveedores", "/admin/proveedores/nuevo", "/admin/promociones", "/admin/promociones/nueva", "/admin/promociones/cupones/nuevo",
        "/admin/usuarios", "/admin/usuarios/roles", "/admin/resenas", "/admin/mensajes", "/admin/configuracion"
    ];

    [Fact]
    public async Task Administrador_abre_todas_las_paginas_del_panel()
    {
        var nav = await ComoAsync("admin@t.bo");
        foreach (var url in PaginasPanel)
        {
            var r = await nav.Http.GetAsync(url);
            Assert.True(r.StatusCode == HttpStatusCode.OK, $"{url} → {(int)r.StatusCode}");
        }
        int productoId;
        using (var s = _app.Services.CreateScope()) productoId = await s.ServiceProvider.GetRequiredService<AppDbContext>().Productos.Select(p => p.Id).FirstAsync();
        Assert.Equal(HttpStatusCode.OK, (await nav.Http.GetAsync($"/admin/productos/{productoId}")).StatusCode);
    }

    [Fact]
    public async Task Visitante_va_al_login_y_cliente_recibe_403()
    {
        var anonimo = await _app.NuevoNavegador().Http.GetAsync("/admin/productos");
        Assert.Contains("/cuenta/login", anonimo.Headers.Location?.OriginalString);

        var cliente = await ComoAsync("cliente@t.bo");
        foreach (var url in new[] { "/admin", "/admin/productos", "/admin/pedidos", "/admin/usuarios" })
        {
            var r = await cliente.Http.GetAsync(url);
            Assert.True(r.StatusCode is HttpStatusCode.Forbidden || (r.StatusCode == HttpStatusCode.Redirect && r.Headers.Location!.OriginalString.Contains("acceso-denegado")),
                $"{url} → {(int)r.StatusCode} {r.Headers.Location}");
        }
    }

    [Fact]
    public async Task Vendedor_solo_entra_a_los_modulos_de_su_matriz()
    {
        var nav = await ComoAsync("vende@t.bo");
        foreach (var permitido in new[] { "/admin", "/admin/pedidos", "/admin/inventario", "/admin/proveedores", "/admin/productos" })
            Assert.Equal(HttpStatusCode.OK, (await nav.Http.GetAsync(permitido)).StatusCode);

        foreach (var prohibido in new[] { "/admin/usuarios", "/admin/configuracion", "/admin/categorias", "/admin/promociones", "/admin/productos/nuevo" })
        {
            var r = await nav.Http.GetAsync(prohibido);
            Assert.True(r.StatusCode == HttpStatusCode.Redirect && r.Headers.Location!.OriginalString.Contains("acceso-denegado"), $"{prohibido} → {(int)r.StatusCode}");
        }
    }

    [Fact]
    public async Task Vendedor_no_puede_crear_productos_ni_cambiar_la_configuracion_aunque_envie_el_formulario()
    {
        var admin = await ComoAsync("admin@t.bo");
        var token = await admin.TokenDeAsync("/admin/productos/nuevo"); // token válido de otra sesión no sirve; se usa el del vendedor
        var nav = await ComoAsync("vende@t.bo");
        var tokenVendedor = await nav.TokenDeAsync("/admin/pedidos");

        var crear = await nav.Http.PostAsync("/admin/productos/nuevo", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = tokenVendedor, ["Sku"] = "HACK-1", ["Nombre"] = "Producto pirata", ["DescripcionCorta"] = "x",
            ["Descripcion"] = "x", ["CategoriaId"] = "1", ["MarcaId"] = "1", ["Precio"] = "1", ["PrecioCosto"] = "1"
        }));
        Assert.Contains("acceso-denegado", crear.Headers.Location?.OriginalString);

        var config = await nav.Http.PostAsync("/admin/configuracion", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = tokenVendedor, ["NombreTienda"] = "Hackeada", ["Moneda"] = "$", ["InstruccionesTransferencia"] = "x"
        }));
        Assert.Contains("acceso-denegado", config.Headers.Location?.OriginalString);

        using var s = _app.Services.CreateScope();
        var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.Productos.AnyAsync(p => p.Sku == "HACK-1"));
        Assert.NotEqual("Hackeada", (await db.Ajustes.SingleAsync(a => a.Clave == ClavesAjuste.NombreTienda)).Valor);
    }

    [Fact]
    public async Task Admin_crea_producto_con_precio_decimal_y_stock_inicial()
    {
        var nav = await ComoAsync("admin@t.bo");
        int cat, mar;
        using (var s = _app.Services.CreateScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
            (cat, mar) = (await db.Categorias.Select(c => c.Id).FirstAsync(), await db.Marcas.Select(m => m.Id).FirstAsync());
        }
        var r = await nav.EnviarAsync("/admin/productos/nuevo", "/admin/productos/nuevo", new()
        {
            ["Sku"] = "nuevo-1", ["Nombre"] = "Teclado Nuevo", ["DescripcionCorta"] = "Corto", ["Descripcion"] = "Largo",
            ["CategoriaId"] = cat.ToString(), ["MarcaId"] = mar.ToString(), ["Precio"] = "1590.50", ["PrecioCosto"] = "1000",
            ["StockInicial"] = "7", ["StockMinimo"] = "2", ["GarantiaMeses"] = "12", ["Estado"] = "Activo",
            ["Especificaciones"] = "Teclado | Switches | Rojos\nTeclado | Formato | TKL"
        });
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);

        using var scope = _app.Services.CreateScope();
        var p = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Productos.Include(x => x.Especificaciones).SingleAsync(x => x.Sku == "NUEVO-1");
        Assert.Equal(1590.50m, p.Precio);
        Assert.Equal(7, p.Stock);
        Assert.Equal(2, p.Especificaciones.Count);
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<AppDbContext>().MovimientosInventario.CountAsync(m => m.ProductoId == p.Id));
    }

    [Fact]
    public async Task Enlace_al_panel_solo_aparece_para_el_personal()
    {
        var admin = await (await (await ComoAsync("admin@t.bo")).Http.GetAsync("/")).Content.ReadAsStringAsync();
        var cliente = await (await (await ComoAsync("cliente@t.bo")).Http.GetAsync("/")).Content.ReadAsStringAsync();
        Assert.Contains("Panel de administración", admin);
        Assert.DoesNotContain("Panel de administración", cliente);
    }
}
