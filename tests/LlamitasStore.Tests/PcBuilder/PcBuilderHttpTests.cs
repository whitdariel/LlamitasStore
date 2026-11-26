using System.Net;
using LlamitasStore.Data;
using LlamitasStore.Tests.Soporte;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LlamitasStore.Tests.PcBuilder;

/// <summary>El PC Builder por HTTP con la app real: páginas, carrito, guardado y propiedad de las configuraciones.</summary>
public sealed class PcBuilderHttpTests : IAsyncLifetime
{
    private readonly FabricaApp _app = new();
    private EscenarioPc _e = null!;

    public Task InitializeAsync() => _app.InicializarAsync(async db => _e = await EscenarioPc.CrearAsync(db));
    public Task DisposeAsync() { _app.Dispose(); return Task.CompletedTask; }

    private static string Ubicacion(HttpResponseMessage r) => r.Headers.Location?.OriginalString ?? "";
    private static async Task<string> Html(Navegador n, string url)
    {
        var r = await n.Http.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return WebUtility.HtmlDecode(await r.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Pagina_vacia_empieza_por_el_procesador_y_esta_en_el_menu()
    {
        var html = await Html(_app.NuevoNavegador(), "/pc-builder");

        Assert.Contains("Paso 1 de 8", html);
        Assert.Contains("Pieza CPU-AM5", html);
        Assert.DoesNotContain("Pieza PLACA-AM5", html); // solo opciones del paso actual
        Assert.Contains("href=\"/pc-builder\"", await Html(_app.NuevoNavegador(), "/"));
    }

    [Fact]
    public async Task Armado_compatible_permite_agregar_todo_al_carrito()
    {
        var nav = _app.NuevoNavegador();
        var html = await Html(nav, _e.UrlCompleta);
        Assert.Contains("Configuración compatible", html);

        var r = await nav.EnviarAsync(_e.UrlCompleta, "/pc-builder/al-carrito", new()
        {
            ["cpu"] = _e.CpuAm5.ToString(), ["placa"] = _e.PlacaAm5.ToString(), ["ram"] = $"{_e.RamDdr5}*2", ["alm"] = _e.Ssd.ToString()
        });
        Assert.Equal("/carrito", Ubicacion(r));

        var carrito = await Html(nav, "/carrito");
        Assert.Contains("Agregaste 4 componente(s)", carrito);
        Assert.Contains("Pieza RAM-DDR5", carrito);
    }

    [Fact]
    public async Task Incompatibilidad_se_explica_y_bloquea_el_boton()
    {
        var html = await Html(_app.NuevoNavegador(), $"/pc-builder?cpu={_e.CpuIntel}&placa={_e.PlacaAm5}&paso=placa");

        // Socket distinto + CPU sin disipador + CPU sin gráficos integrados (y sin GPU).
        Assert.Contains("3 incompatibilidad(es)", html);
        Assert.Contains("socket LGA1700 y la placa madre socket AM5", html);
        Assert.Contains("no incluye disipador", html);
        Assert.Contains("disabled", html[html.IndexOf("/pc-builder/al-carrito", StringComparison.Ordinal)..]);
        // La placa elegida se muestra aunque sea incompatible; la compatible aparece como alternativa.
        Assert.Contains("Pieza PLACA-INTEL", html);
        Assert.Contains("Mostrar también incompatibles", html);

        // Sin placa elegida, la AM5 incompatible se oculta; el enlace "mostrar todas" la muestra.
        var soloCompatibles = await Html(_app.NuevoNavegador(), $"/pc-builder?cpu={_e.CpuIntel}&paso=placa");
        Assert.DoesNotContain("Pieza PLACA-AM5", soloCompatibles);
        var enlace = System.Text.RegularExpressions.Regex.Match(soloCompatibles, "href=\"([^\"]+todos=true)\"").Groups[1].Value;
        Assert.Contains("Pieza PLACA-AM5", await Html(_app.NuevoNavegador(), enlace));
    }

    [Fact]
    public async Task Cantidad_de_kits_redirige_con_la_seleccion_y_limita_el_maximo()
    {
        var r = await _app.NuevoNavegador().Http.GetAsync($"/pc-builder/cantidad?cpu={_e.CpuAm5}&ram={_e.RamDdr5}&tipo=ram&id={_e.RamDdr5}&cantidad=9");
        Assert.Equal($"/pc-builder?cpu={_e.CpuAm5}&ram={_e.RamDdr5}%2A4&paso=ram", Ubicacion(r));
    }

    [Fact]
    public async Task Guardar_exige_sesion_y_la_configuracion_es_privada()
    {
        var visitante = _app.NuevoNavegador();
        var sinSesion = await visitante.EnviarAsync(_e.UrlCompleta, "/pc-builder/guardar", new() { ["cpu"] = _e.CpuAm5.ToString(), ["nombre"] = "Mía" });
        Assert.Contains("/cuenta/login", Ubicacion(sinSesion));

        var ana = _app.NuevoNavegador();
        await ana.RegistrarAsync("ana.pc@prueba.bo");
        var guardar = await ana.EnviarAsync(_e.UrlCompleta, "/pc-builder/guardar", new()
        {
            ["cpu"] = _e.CpuAm5.ToString(), ["placa"] = _e.PlacaAm5.ToString(), ["nombre"] = "Gamer de Ana"
        });
        Assert.Matches(@"^/pc-builder\?cpu=\d+&placa=\d+&config=\d+$", Ubicacion(guardar));
        var configId = Ubicacion(guardar).Split("config=")[1];

        var abierta = await Html(ana, Ubicacion(guardar));
        Assert.Contains("Configurador · Gamer de Ana", abierta);
        Assert.Contains("Gamer de Ana", await Html(ana, "/cuenta/configuraciones"));

        // Otra persona no ve el nombre ni puede borrarla.
        var beto = _app.NuevoNavegador();
        await beto.RegistrarAsync("beto.pc@prueba.bo");
        Assert.DoesNotContain("Gamer de Ana", await Html(beto, Ubicacion(guardar)));
        Assert.DoesNotContain("Gamer de Ana", await Html(beto, "/cuenta/configuraciones"));
        await beto.EnviarAsync("/cuenta/configuraciones", $"/cuenta/configuraciones/{configId}/eliminar", new());

        using (var scope = _app.Services.CreateScope())
            Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<AppDbContext>().ConfiguracionesPc.CountAsync());

        // La dueña sí puede borrarla.
        await ana.EnviarAsync("/cuenta/configuraciones", $"/cuenta/configuraciones/{configId}/eliminar", new());
        using (var scope = _app.Services.CreateScope())
            Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<AppDbContext>().ConfiguracionesPc.CountAsync());
    }

    [Fact]
    public async Task Post_sin_token_csrf_se_rechaza()
    {
        var r = await _app.NuevoNavegador().Http.PostAsync("/pc-builder/al-carrito",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["cpu"] = _e.CpuAm5.ToString() }));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }
}
