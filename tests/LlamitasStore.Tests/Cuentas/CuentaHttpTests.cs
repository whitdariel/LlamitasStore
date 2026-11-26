using System.Net;
using System.Text.Json;
using LlamitasStore.Data;
using LlamitasStore.Models.Entities;
using LlamitasStore.Tests.Soporte;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LlamitasStore.Tests.Cuentas;

/// <summary>Direcciones y favoritos por HTTP, incluida la protección contra acceso a datos de otro usuario.</summary>
public sealed class CuentaHttpTests : IAsyncLifetime
{
    private readonly FabricaApp _app = new();
    private int _productoId;

    public Task InitializeAsync() => _app.InicializarAsync(async db =>
    {
        var producto = new Producto
        {
            Sku = "T-1", Nombre = "Ratón de prueba", Slug = "raton-de-prueba", DescripcionCorta = "c", Descripcion = "d",
            Categoria = new Categoria { Nombre = "Ratones", Slug = "ratones" }, Marca = new Marca { Nombre = "Marca", Slug = "marca" },
            Precio = 100, PrecioCosto = 50, Stock = 5
        };
        db.Productos.Add(producto);
        await db.SaveChangesAsync();
        _productoId = producto.Id;
    });

    public Task DisposeAsync() { _app.Dispose(); return Task.CompletedTask; }

    private static Dictionary<string, string> Direccion(string alias) => new()
    {
        ["Alias"] = alias, ["Destinatario"] = "Ana Prueba", ["Telefono"] = "+591 71234567", ["Departamento"] = "La Paz",
        ["Ciudad"] = "Viacha", ["DireccionLinea"] = "Calle 1 #23", ["Referencia"] = ""
    };

    [Fact]
    public async Task Crear_direccion_y_no_poder_tocar_la_de_otro_usuario()
    {
        var ana = _app.NuevoNavegador();
        await ana.RegistrarAsync("ana.dir@prueba.bo");
        var creada = await ana.EnviarAsync("/cuenta/direcciones/nueva", "/cuenta/direcciones/nueva", Direccion("Casa"));
        Assert.Equal(HttpStatusCode.Redirect, creada.StatusCode);

        int idAna;
        using (var scope = _app.Services.CreateScope())
            idAna = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Direcciones.Select(d => d.Id).SingleAsync();

        var beto = _app.NuevoNavegador();
        await beto.RegistrarAsync("beto.dir@prueba.bo");

        Assert.Equal(HttpStatusCode.NotFound, (await beto.Http.GetAsync($"/cuenta/direcciones/{idAna}")).StatusCode);
        var borrar = await beto.EnviarAsync("/cuenta/direcciones", $"/cuenta/direcciones/{idAna}/eliminar", new());
        Assert.Equal(HttpStatusCode.NotFound, borrar.StatusCode);
        var editar = await beto.EnviarAsync("/cuenta/direcciones/nueva", $"/cuenta/direcciones/{idAna}", Direccion("Hackeada"));
        Assert.Equal(HttpStatusCode.NotFound, editar.StatusCode);

        using var s2 = _app.Services.CreateScope();
        var d = await s2.ServiceProvider.GetRequiredService<AppDbContext>().Direcciones.SingleAsync();
        Assert.Equal("Casa", d.Alias);
        Assert.True(d.EsPredeterminada);
    }

    [Fact]
    public async Task Direccion_con_departamento_inexistente_no_se_guarda()
    {
        var nav = _app.NuevoNavegador();
        await nav.RegistrarAsync("depto@prueba.bo");
        var datos = Direccion("Casa");
        datos["Departamento"] = "Narnia";

        var r = await nav.EnviarAsync("/cuenta/direcciones/nueva", "/cuenta/direcciones/nueva", datos);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Contains("Elige un departamento", await r.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Favorito_se_alterna_por_fetch_y_aparece_en_la_lista()
    {
        var nav = _app.NuevoNavegador();
        await nav.RegistrarAsync("fav@prueba.bo");
        var fetch = new Dictionary<string, string> { ["X-Requested-With"] = "fetch" };

        var r1 = await nav.EnviarAsync("/catalogo", $"/cuenta/favoritos/{_productoId}/alternar", new(), fetch);
        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);
        var j1 = JsonDocument.Parse(await r1.Content.ReadAsStringAsync()).RootElement;
        Assert.True(j1.GetProperty("favorito").GetBoolean());
        Assert.Equal(1, j1.GetProperty("total").GetInt32());

        Assert.Contains("Ratón de prueba", await (await nav.Http.GetAsync("/cuenta/favoritos")).Content.ReadAsStringAsync());

        var r2 = await nav.EnviarAsync("/catalogo", $"/cuenta/favoritos/{_productoId}/alternar", new(), fetch);
        var j2 = JsonDocument.Parse(await r2.Content.ReadAsStringAsync()).RootElement;
        Assert.False(j2.GetProperty("favorito").GetBoolean());
        Assert.Equal(0, j2.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Favorito_de_producto_inexistente_responde_404()
    {
        var nav = _app.NuevoNavegador();
        await nav.RegistrarAsync("fav404@prueba.bo");
        var r = await nav.EnviarAsync("/catalogo", "/cuenta/favoritos/999999/alternar", new(), new() { ["X-Requested-With"] = "fetch" });
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }

    [Fact]
    public async Task Visitante_ve_corazon_que_lleva_al_login()
    {
        var html = await (await _app.NuevoNavegador().Http.GetAsync("/catalogo")).Content.ReadAsStringAsync();
        Assert.Contains("Ingresa para guardar en favoritos", html);
        Assert.DoesNotContain("/alternar", html);
    }
}
