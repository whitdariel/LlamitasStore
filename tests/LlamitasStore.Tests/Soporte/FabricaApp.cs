using System.Net;
using System.Text.RegularExpressions;
using LlamitasStore.Data;
using LlamitasStore.Data.Semilla;
using LlamitasStore.Services.Correo;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LlamitasStore.Tests.Soporte;

/// <summary>Correos capturados en memoria (las pruebas leen el enlace de recuperación).</summary>
public sealed class EnviadorMemoria : IEnviadorCorreo
{
    public List<(string Para, string Asunto, string Html)> Enviados { get; } = new();
    public Task EnviarAsync(string para, string asunto, string html, CancellationToken ct = default)
    {
        lock (Enviados) Enviados.Add((para, asunto, html));
        return Task.CompletedTask;
    }
}

/// <summary>
/// La aplicación real (Program.cs completo: Identity, CSRF, cookies, límites, rutas) sobre SQLite en memoria.
/// Entorno "Testing": cookies solo HTTPS como en producción, por eso el cliente usa https://localhost.
/// </summary>
public sealed class FabricaApp : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _conexion = new("DataSource=:memory:");
    public EnviadorMemoria Correos { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:DefaultConnection", "Server=no-se-usa-en-pruebas");
        builder.UseSetting("Database:MigrarAlIniciar", "false");
        builder.UseSetting("Seed:DatosDemo", "false");
        builder.UseSetting("Limites:FormulariosPorMinuto", "10000");
        builder.UseSetting("Limites:BusquedasPorMinuto", "10000");

        builder.ConfigureServices(s =>
        {
            s.RemoveAll<DbContextOptions<AppDbContext>>();
            s.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
            s.RemoveAll<AppDbContext>();
            _conexion.Open();
            var opciones = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conexion).Options;
            s.AddScoped<AppDbContext>(_ => new AppDbContextSqlite(opciones));

            s.RemoveAll<IEnviadorCorreo>();
            s.AddSingleton<IEnviadorCorreo>(Correos);
        });
    }

    /// <summary>Crea el esquema y los datos base (roles, módulos, ajustes). Llamar una vez por fábrica.</summary>
    public async Task InicializarAsync(Func<AppDbContext, Task>? datosExtra = null)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();
        await scope.ServiceProvider.GetRequiredService<DatosIniciales>().EjecutarAsync();
        if (datosExtra is not null) await datosExtra(db);
    }

    public Navegador NuevoNavegador() => new(CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
        HandleCookies = true
    }));

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _conexion.Dispose();
    }
}

/// <summary>Cliente HTTP que se comporta como un navegador: guarda cookies y envía el token CSRF de cada formulario.</summary>
public sealed partial class Navegador
{
    public HttpClient Http { get; }
    public Navegador(HttpClient http) => Http = http;

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenCsrf();

    public async Task<string> TokenDeAsync(string urlFormulario)
    {
        var html = await (await Http.GetAsync(urlFormulario)).Content.ReadAsStringAsync();
        var m = TokenCsrf().Match(html);
        Assert.True(m.Success, $"La página {urlFormulario} no tiene token CSRF");
        return WebUtility.HtmlDecode(m.Groups[1].Value);
    }

    /// <summary>GET del formulario (para obtener el token) y POST con los campos.</summary>
    public async Task<HttpResponseMessage> EnviarAsync(string urlFormulario, string urlAccion, Dictionary<string, string> campos, Dictionary<string, string>? cabeceras = null)
    {
        var token = await TokenDeAsync(urlFormulario);
        var datos = new Dictionary<string, string>(campos) { ["__RequestVerificationToken"] = token };
        var peticion = new HttpRequestMessage(HttpMethod.Post, urlAccion) { Content = new FormUrlEncodedContent(datos) };
        foreach (var (k, v) in cabeceras ?? []) peticion.Headers.Add(k, v);
        return await Http.SendAsync(peticion);
    }

    public Task<HttpResponseMessage> RegistrarAsync(string email, string password = "Llama1234", string nombre = "Ana") =>
        EnviarAsync("/cuenta/registro", "/cuenta/registro", new()
        {
            ["Nombre"] = nombre, ["Apellido"] = "Prueba", ["Email"] = email,
            ["Password"] = password, ["ConfirmarPassword"] = password, ["AceptaPrivacidad"] = "true"
        });

    public Task<HttpResponseMessage> IngresarAsync(string email, string password, string? returnUrl = null) =>
        EnviarAsync("/cuenta/login", "/cuenta/login", new()
        {
            ["Email"] = email, ["Password"] = password, ["ReturnUrl"] = returnUrl ?? ""
        });

    public Task<HttpResponseMessage> SalirAsync() => EnviarAsync("/cuenta", "/cuenta/logout", new());
}
