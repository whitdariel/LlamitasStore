using System.Text.RegularExpressions;
using LlamitasStore.Tests.Soporte;

namespace LlamitasStore.Tests.Cuentas;

public sealed class SeguridadTests : IAsyncLifetime
{
    private readonly FabricaApp _app = new();
    public Task InitializeAsync() => _app.InicializarAsync();
    public Task DisposeAsync() { _app.Dispose(); return Task.CompletedTask; }

    [Fact]
    public async Task Nombre_con_html_se_muestra_escapado_sin_ejecutarse()
    {
        var nav = _app.NuevoNavegador();
        await nav.RegistrarAsync("xss@prueba.bo", nombre: "<script>alert(1)</script>");

        var html = await (await nav.Http.GetAsync("/cuenta")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("<script>alert(1)</script>", html);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html);
    }

    [Fact]
    public async Task Las_respuestas_incluyen_cabeceras_de_seguridad()
    {
        var r = await _app.NuevoNavegador().Http.GetAsync("/cuenta/login");
        Assert.True(r.Headers.Contains("Content-Security-Policy"));
        Assert.Equal("DENY", r.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("nosniff", r.Headers.GetValues("X-Content-Type-Options").Single());
    }

    [Fact]
    public async Task Cookie_de_sesion_es_httponly_y_secure()
    {
        var r = await _app.NuevoNavegador().RegistrarAsync("cookie@prueba.bo");
        var sesion = r.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith(".LlamitasStore.Sesion="));
        Assert.Contains("httponly", sesion, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", sesion, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", sesion, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Todo_formulario_post_con_action_manual_lleva_token_csrf()
    {
        // El tag helper NO agrega el token cuando el action está escrito a mano: hay que pedirlo con asp-antiforgery="true".
        var raiz = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(raiz.FullName, "LlamitasStore.sln"))) raiz = raiz.Parent!;
        // Proyecto MVC en la raíz del repositorio: vistas de la tienda y del panel (Areas).
        var vistas = new[] { "Views", "Areas" }
            .SelectMany(c => Directory.EnumerateFiles(Path.Combine(raiz.FullName, c), "*.cshtml", SearchOption.AllDirectories));

        var sinToken = new List<string>();
        foreach (var archivo in vistas)
        {
            foreach (Match form in Regex.Matches(File.ReadAllText(archivo), @"<form\b[^>]*>", RegexOptions.IgnoreCase))
            {
                var etiqueta = form.Value;
                var esPost = Regex.IsMatch(etiqueta, @"method=""post""", RegexOptions.IgnoreCase);
                var actionManual = Regex.IsMatch(etiqueta, @"\saction=""", RegexOptions.IgnoreCase);
                if (esPost && actionManual && !etiqueta.Contains("asp-antiforgery=\"true\""))
                    sinToken.Add($"{Path.GetFileName(archivo)}: {etiqueta}");
            }
        }
        Assert.True(sinToken.Count == 0, "Formularios POST sin token CSRF:\n" + string.Join('\n', sinToken));
    }
}
