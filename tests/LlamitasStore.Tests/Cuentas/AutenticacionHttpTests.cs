using System.Net;
using System.Text.RegularExpressions;
using LlamitasStore.Data;
using LlamitasStore.Models.Entities;
using LlamitasStore.Tests.Soporte;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LlamitasStore.Tests.Cuentas;

/// <summary>Registro, login, sesión y recuperación de contraseña contra la aplicación completa (HTTP real en memoria).</summary>
public sealed class AutenticacionHttpTests : IAsyncLifetime
{
    private readonly FabricaApp _app = new();

    public Task InitializeAsync() => _app.InicializarAsync();
    public Task DisposeAsync() { _app.Dispose(); return Task.CompletedTask; }

    private static string Ubicacion(HttpResponseMessage r) => r.Headers.Location?.OriginalString ?? "";

    [Fact]
    public async Task Registro_crea_cliente_inicia_sesion_y_guarda_contrasena_hasheada()
    {
        var nav = _app.NuevoNavegador();
        var r = await nav.RegistrarAsync("ana@prueba.bo");

        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        var cuenta = await nav.Http.GetAsync("/cuenta");
        Assert.Equal(HttpStatusCode.OK, cuenta.StatusCode);
        Assert.Contains("Hola, Ana", await cuenta.Content.ReadAsStringAsync());

        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var u = await db.Users.SingleAsync(x => x.Email == "ana@prueba.bo");
        Assert.DoesNotContain("Llama1234", u.PasswordHash);
        var rol = await db.UserRoles.Where(ur => ur.UserId == u.Id).Join(db.Roles, ur => ur.RoleId, ro => ro.Id, (_, ro) => ro.Name).SingleAsync();
        Assert.Equal(Roles.Cliente, rol);
        Assert.Single(_app.Correos.Enviados, c => c.Para == "ana@prueba.bo" && c.Asunto.Contains("Bienvenido"));
    }

    [Fact]
    public async Task Registro_rechaza_correo_repetido_y_contrasena_debil()
    {
        await _app.NuevoNavegador().RegistrarAsync("repetido@prueba.bo");

        var r = await _app.NuevoNavegador().RegistrarAsync("repetido@prueba.bo");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode); // vuelve a mostrar el formulario
        Assert.Contains("ya está registrado", await r.Content.ReadAsStringAsync());

        var debil = await _app.NuevoNavegador().RegistrarAsync("debil@prueba.bo", password: "llamitas1");
        Assert.Contains("mayúscula", await debil.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Login_correcto_y_cierre_de_sesion()
    {
        await _app.NuevoNavegador().RegistrarAsync("luis@prueba.bo");
        var nav = _app.NuevoNavegador();

        var r = await nav.IngresarAsync("luis@prueba.bo", "Llama1234");
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await nav.Http.GetAsync("/cuenta")).StatusCode);

        await nav.SalirAsync();
        var despues = await nav.Http.GetAsync("/cuenta");
        Assert.Equal(HttpStatusCode.Redirect, despues.StatusCode);
        Assert.StartsWith("https://localhost/cuenta/login", Ubicacion(despues));
    }

    [Fact]
    public async Task Login_incorrecto_no_revela_si_el_correo_existe()
    {
        await _app.NuevoNavegador().RegistrarAsync("existe@prueba.bo");
        var a = await (await _app.NuevoNavegador().IngresarAsync("existe@prueba.bo", "Mala1234")).Content.ReadAsStringAsync();
        var b = await (await _app.NuevoNavegador().IngresarAsync("noexiste@prueba.bo", "Mala1234")).Content.ReadAsStringAsync();

        Assert.Contains("Correo o contraseña incorrectos", a);
        Assert.Contains("Correo o contraseña incorrectos", b);
    }

    [Fact]
    public async Task Cinco_intentos_fallidos_bloquean_la_cuenta_aunque_luego_la_clave_sea_correcta()
    {
        await _app.NuevoNavegador().RegistrarAsync("bloqueo@prueba.bo");
        var nav = _app.NuevoNavegador();
        for (var i = 0; i < 5; i++) await nav.IngresarAsync("bloqueo@prueba.bo", "Incorrecta1");

        var r = await nav.IngresarAsync("bloqueo@prueba.bo", "Llama1234");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Contains("bloqueada", await r.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("https://sitio-malicioso.com/robar", "/")]
    [InlineData("//sitio-malicioso.com", "/")]
    [InlineData("/catalogo/ratones", "/catalogo/ratones")]
    public async Task ReturnUrl_solo_permite_destinos_del_propio_sitio(string returnUrl, string esperado)
    {
        await _app.NuevoNavegador().RegistrarAsync($"redir{Math.Abs(returnUrl.GetHashCode())}@prueba.bo");
        var r = await _app.NuevoNavegador().IngresarAsync($"redir{Math.Abs(returnUrl.GetHashCode())}@prueba.bo", "Llama1234", returnUrl);

        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Equal(esperado, Ubicacion(r));
    }

    [Fact]
    public async Task Usuario_desactivado_no_puede_ingresar()
    {
        await _app.NuevoNavegador().RegistrarAsync("inactivo@prueba.bo");
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var u = await db.Users.SingleAsync(x => x.Email == "inactivo@prueba.bo");
            u.Estado = EstadoUsuario.Inactivo;
            await db.SaveChangesAsync();
        }

        var r = await _app.NuevoNavegador().IngresarAsync("inactivo@prueba.bo", "Llama1234");
        Assert.Contains("desactivada", await r.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Post_sin_token_csrf_es_rechazado()
    {
        var nav = _app.NuevoNavegador();
        var r = await nav.Http.PostAsync("/cuenta/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = "x@prueba.bo", ["Password"] = "Llama1234"
        }));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task Paginas_privadas_exigen_sesion()
    {
        var nav = _app.NuevoNavegador();
        foreach (var url in new[] { "/cuenta", "/cuenta/perfil", "/cuenta/direcciones", "/cuenta/favoritos", "/cuenta/seguridad" })
        {
            var r = await nav.Http.GetAsync(url);
            Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
            Assert.Contains("/cuenta/login", Ubicacion(r));
        }
    }

    [Fact]
    public async Task Recuperacion_de_contrasena_de_punta_a_punta_y_enlace_de_un_solo_uso()
    {
        await _app.NuevoNavegador().RegistrarAsync("olvido@prueba.bo");
        var nav = _app.NuevoNavegador();

        // Correo inexistente: misma respuesta y ningún correo enviado.
        var antes = _app.Correos.Enviados.Count;
        var r1 = await nav.EnviarAsync("/cuenta/olvide-contrasena", "/cuenta/olvide-contrasena", new() { ["Email"] = "nadie@prueba.bo" });
        Assert.Equal("/cuenta/olvide-contrasena/enviado", Ubicacion(r1));
        Assert.Equal(antes, _app.Correos.Enviados.Count);

        // Correo existente: llega el enlace.
        var r2 = await nav.EnviarAsync("/cuenta/olvide-contrasena", "/cuenta/olvide-contrasena", new() { ["Email"] = "olvido@prueba.bo" });
        Assert.Equal("/cuenta/olvide-contrasena/enviado", Ubicacion(r2));
        var correo = _app.Correos.Enviados.Last(c => c.Para == "olvido@prueba.bo");
        var enlace = WebUtility.HtmlDecode(Regex.Match(correo.Html, "href=\"(https://localhost/cuenta/restablecer-contrasena[^\"]+)\"").Groups[1].Value);
        Assert.False(string.IsNullOrEmpty(enlace));
        var codigo = Uri.UnescapeDataString(Regex.Match(enlace, "codigo=([^&]+)").Groups[1].Value);

        var campos = new Dictionary<string, string>
        {
            ["Email"] = "olvido@prueba.bo", ["Token"] = codigo, ["Password"] = "NuevaLlama9", ["ConfirmarPassword"] = "NuevaLlama9"
        };
        var r3 = await nav.EnviarAsync(enlace, "/cuenta/restablecer-contrasena", campos);
        Assert.Equal("/cuenta/restablecer-contrasena/listo", Ubicacion(r3));

        Assert.Contains("incorrectos", await (await _app.NuevoNavegador().IngresarAsync("olvido@prueba.bo", "Llama1234")).Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Redirect, (await _app.NuevoNavegador().IngresarAsync("olvido@prueba.bo", "NuevaLlama9")).StatusCode);

        // Reusar el mismo enlace ya no funciona.
        campos["Password"] = campos["ConfirmarPassword"] = "OtraLlama77";
        var r4 = await nav.EnviarAsync(enlace, "/cuenta/restablecer-contrasena", campos);
        Assert.Contains("no es válido o ya expiró", await r4.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Cambiar_contrasena_exige_la_actual()
    {
        var nav = _app.NuevoNavegador();
        await nav.RegistrarAsync("cambio@prueba.bo");

        var mal = await nav.EnviarAsync("/cuenta/seguridad", "/cuenta/seguridad", new()
        {
            ["Actual"] = "NoEsLaMia1", ["Nueva"] = "OtraLlama22", ["Confirmar"] = "OtraLlama22"
        });
        Assert.Contains("contraseña actual no es correcta", await mal.Content.ReadAsStringAsync());

        var bien = await nav.EnviarAsync("/cuenta/seguridad", "/cuenta/seguridad", new()
        {
            ["Actual"] = "Llama1234", ["Nueva"] = "OtraLlama22", ["Confirmar"] = "OtraLlama22"
        });
        Assert.Equal(HttpStatusCode.Redirect, bien.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await _app.NuevoNavegador().IngresarAsync("cambio@prueba.bo", "OtraLlama22")).StatusCode);
    }
}
