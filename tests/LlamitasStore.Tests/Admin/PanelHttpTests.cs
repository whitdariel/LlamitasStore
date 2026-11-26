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
    public async Task Admin_edita_producto_gestiona_imagenes_y_lo_da_de_baja()
    {
        var nav = await ComoAsync("admin@t.bo");
        int id, cat, mar;
        using (var s = _app.Services.CreateScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
            var p = await db.Productos.SingleAsync(x => x.Sku == "P-1");
            (id, cat, mar) = (p.Id, p.CategoriaId, p.MarcaId);
        }

        // 1) Editar datos, precio con oferta, especificaciones y compatibilidad.
        var editar = await nav.EnviarAsync($"/admin/productos/{id}", $"/admin/productos/{id}", new()
        {
            ["Sku"] = "P-1", ["Nombre"] = "Ratón Pro Editado", ["DescripcionCorta"] = "Nuevo corto", ["Descripcion"] = "Nuevo largo",
            ["CategoriaId"] = cat.ToString(), ["MarcaId"] = mar.ToString(), ["Precio"] = "120.00", ["PrecioOferta"] = "99.90",
            ["PrecioCosto"] = "50", ["StockMinimo"] = "1", ["GarantiaMeses"] = "24", ["Estado"] = "Activo", ["Destacado"] = "true",
            ["Especificaciones"] = "Sensor | DPI | 26000\nSensor | Tipo | Óptico", ["ConsumoW"] = "2"
        });
        Assert.Equal($"/admin/productos/{id}", editar.Headers.Location?.OriginalString);

        using (var s = _app.Services.CreateScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
            var p = await db.Productos.Include(x => x.Especificaciones).Include(x => x.Compatibilidad).SingleAsync(x => x.Id == id);
            Assert.Equal(("Ratón Pro Editado", "raton-pro-editado", 120m, 99.90m, 24, true), (p.Nombre, p.Slug, p.Precio, p.PrecioOferta!.Value, p.GarantiaMeses, p.Destacado));
            Assert.Equal(5, p.Stock); // el stock no se toca desde la ficha (solo desde Inventario)
            Assert.Equal(["DPI", "Tipo"], p.Especificaciones.OrderBy(e => e.Orden).Select(e => e.Nombre));
            Assert.Equal(2, p.Compatibilidad!.ConsumoW);
            var h = await db.HistorialPrecios.SingleAsync(x => x.ProductoId == id);
            Assert.Equal((100m, 120m, (decimal?)null, (decimal?)99.90m), (h.PrecioAnterior, h.PrecioNuevo, h.PrecioOfertaAnterior, h.PrecioOfertaNuevo));
        }

        // Validación: oferta mayor que el precio se rechaza y no se guarda.
        var invalido = await nav.EnviarAsync($"/admin/productos/{id}", $"/admin/productos/{id}", new()
        {
            ["Sku"] = "P-1", ["Nombre"] = "Ratón Pro Editado", ["DescripcionCorta"] = "c", ["Descripcion"] = "d",
            ["CategoriaId"] = cat.ToString(), ["MarcaId"] = mar.ToString(), ["Precio"] = "100", ["PrecioOferta"] = "150", ["PrecioCosto"] = "1", ["Estado"] = "Activo"
        });
        Assert.Equal(HttpStatusCode.OK, invalido.StatusCode);
        Assert.Contains("menor al precio normal", WebUtility.HtmlDecode(await invalido.Content.ReadAsStringAsync()));

        // 2) Imagen real (PNG) se acepta; un archivo que no es imagen, aunque diga .png, se rechaza.
        async Task<HttpResponseMessage> SubirAsync(byte[] bytes, string nombre)
        {
            var token = await nav.TokenDeAsync($"/admin/productos/{id}");
            var form = new MultipartFormDataContent
            {
                { new StringContent(token), "__RequestVerificationToken" },
                { new StringContent("Vista frontal"), "textoAlternativo" }
            };
            var archivo = new ByteArrayContent(bytes);
            archivo.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
            form.Add(archivo, "archivo", nombre);
            return await nav.Http.PostAsync($"/admin/productos/{id}/imagenes", form);
        }
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0x0D, 0x49, 0x48, 0x44, 0x52];
        Assert.Equal(HttpStatusCode.Redirect, (await SubirAsync(png, "foto.png")).StatusCode);
        await SubirAsync("<svg onload=alert(1)>"u8.ToArray(), "falso.png");

        string url; int imagenId;
        using (var s = _app.Services.CreateScope())
        {
            var img = await s.ServiceProvider.GetRequiredService<AppDbContext>().ProductoImagenes.SingleAsync(i => i.ProductoId == id);
            (url, imagenId) = (img.Url, img.Id);
            Assert.True(img.EsPrincipal);
            Assert.Equal("Vista frontal", img.TextoAlternativo);
        }
        Assert.Matches(@"^/uploads/productos/[0-9a-f]{32}\.png$", url); // nombre aleatorio, nunca el del usuario
        var ficha = await (await nav.Http.GetAsync("/producto/raton-pro-editado")).Content.ReadAsStringAsync();
        Assert.Contains(url, ficha);

        // Eliminar la imagen también borra el archivo del disco.
        var ruta = Path.Combine(_app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>().WebRootPath, "uploads", "productos", Path.GetFileName(url));
        Assert.True(File.Exists(ruta));
        await nav.EnviarAsync($"/admin/productos/{id}", $"/admin/productos/{id}/imagenes/{imagenId}/eliminar", new());
        Assert.False(File.Exists(ruta));

        // 3) Baja: deja de verse en la tienda, pero el registro se conserva (pedidos históricos).
        await nav.EnviarAsync("/admin/productos", $"/admin/productos/{id}/baja", new());
        Assert.Equal(HttpStatusCode.NotFound, (await nav.Http.GetAsync("/producto/raton-pro-editado")).StatusCode);
        using (var s = _app.Services.CreateScope())
            Assert.Equal(EstadoProducto.Inactivo, (await s.ServiceProvider.GetRequiredService<AppDbContext>().Productos.SingleAsync(x => x.Id == id)).Estado);
    }

    [Fact]
    public async Task Admin_crea_y_edita_categorias_y_marcas()
    {
        var nav = await ComoAsync("admin@t.bo");
        Assert.Equal(HttpStatusCode.Redirect, (await nav.EnviarAsync("/admin/categorias/nueva", "/admin/categorias/nueva", new()
        {
            ["Nombre"] = "Sillas gamer", ["Descripcion"] = "Comodidad", ["Icono"] = "accesorio", ["Orden"] = "20", ["TipoComponente"] = "Ninguno", ["Activa"] = "true"
        })).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await nav.EnviarAsync("/admin/marcas/nueva", "/admin/marcas/nueva", new()
        {
            ["Nombre"] = "Llama Gear", ["Activa"] = "true"
        })).StatusCode);

        int catId;
        using (var s = _app.Services.CreateScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
            var c = await db.Categorias.SingleAsync(x => x.Nombre == "Sillas gamer");
            Assert.Equal("sillas-gamer", c.Slug);
            Assert.True(await db.Marcas.AnyAsync(m => m.Slug == "llama-gear"));
            catId = c.Id;
        }

        // La categoría nueva aparece en el menú de la tienda; al ocultarla, desaparece.
        Assert.Contains("/catalogo/sillas-gamer", await (await _app.NuevoNavegador().Http.GetAsync("/")).Content.ReadAsStringAsync());
        await nav.EnviarAsync($"/admin/categorias/{catId}", $"/admin/categorias/{catId}", new()
        {
            ["Nombre"] = "Sillas gamer", ["Icono"] = "accesorio", ["Orden"] = "20", ["TipoComponente"] = "Ninguno", ["Activa"] = "false"
        });
        Assert.DoesNotContain("/catalogo/sillas-gamer", await (await _app.NuevoNavegador().Http.GetAsync("/")).Content.ReadAsStringAsync());
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
