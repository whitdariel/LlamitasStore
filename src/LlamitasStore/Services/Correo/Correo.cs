using System.Net;
using System.Net.Mail;
using System.Text.Encodings.Web;
using Microsoft.Extensions.Options;

namespace LlamitasStore.Services.Correo;

public interface IEnviadorCorreo
{
    Task EnviarAsync(string para, string asunto, string html, CancellationToken ct = default);
}

public class OpcionesCorreo
{
    public string Remitente { get; set; } = "no-responder@llamitasstore.local";
    public string NombreRemitente { get; set; } = "LlamitasStore";
    public OpcionesSmtp Smtp { get; set; } = new();
    /// <summary>Solo desarrollo: carpeta donde se guardan los correos cuando no hay SMTP.</summary>
    public string CarpetaDesarrollo { get; set; } = "App_Data/correos";
}

public class OpcionesSmtp
{
    public string? Host { get; set; }
    public int Puerto { get; set; } = 587;
    public string? Usuario { get; set; }
    /// <summary>Configurar con user-secrets o variables de entorno, nunca en appsettings.</summary>
    public string? Password { get; set; }
    public bool UsarSsl { get; set; } = true;
}

/// <summary>Envío real por SMTP (Gmail, Outlook, Mailtrap, el servidor de tu hosting, etc.).</summary>
public class EnviadorSmtp : IEnviadorCorreo
{
    private readonly OpcionesCorreo _opciones;
    private readonly ILogger<EnviadorSmtp> _logger;

    public EnviadorSmtp(IOptions<OpcionesCorreo> opciones, ILogger<EnviadorSmtp> logger)
    {
        _opciones = opciones.Value;
        _logger = logger;
    }

    public async Task EnviarAsync(string para, string asunto, string html, CancellationToken ct = default)
    {
        using var mensaje = new MailMessage
        {
            From = new MailAddress(_opciones.Remitente, _opciones.NombreRemitente),
            Subject = asunto,
            Body = html,
            IsBodyHtml = true
        };
        mensaje.To.Add(para);

        using var cliente = new SmtpClient(_opciones.Smtp.Host, _opciones.Smtp.Puerto)
        {
            EnableSsl = _opciones.Smtp.UsarSsl,
            Credentials = string.IsNullOrEmpty(_opciones.Smtp.Usuario)
                ? null
                : new NetworkCredential(_opciones.Smtp.Usuario, _opciones.Smtp.Password)
        };
        await cliente.SendMailAsync(mensaje, ct);
        _logger.LogInformation("Correo \"{Asunto}\" enviado a {Para}", asunto, para);
    }
}

/// <summary>
/// SOLO DESARROLLO y solo si no hay SMTP: guarda cada correo como archivo .html para poder abrirlo.
/// No se presenta al usuario como "enviado": el log indica dónde quedó el archivo.
/// </summary>
public class EnviadorArchivo : IEnviadorCorreo
{
    private readonly string _carpeta;
    private readonly ILogger<EnviadorArchivo> _logger;

    public EnviadorArchivo(IOptions<OpcionesCorreo> opciones, IWebHostEnvironment entorno, ILogger<EnviadorArchivo> logger)
    {
        _carpeta = Path.Combine(entorno.ContentRootPath, opciones.Value.CarpetaDesarrollo);
        _logger = logger;
    }

    public async Task EnviarAsync(string para, string asunto, string html, CancellationToken ct = default)
    {
        Directory.CreateDirectory(_carpeta);
        var seguro = string.Concat(para.Select(c => char.IsLetterOrDigit(c) ? c : '_'));
        var archivo = Path.Combine(_carpeta, $"{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}-{seguro}.html");
        var contenido = $"<!-- Para: {WebUtility.HtmlEncode(para)} | Asunto: {WebUtility.HtmlEncode(asunto)} -->\n{html}";
        await File.WriteAllTextAsync(archivo, contenido, ct);
        _logger.LogWarning("SMTP no configurado (desarrollo): el correo \"{Asunto}\" para {Para} se guardó en {Archivo}", asunto, para, archivo);
    }
}

/// <summary>Producción sin SMTP: no se puede enviar. Se registra como error para que el administrador lo vea.</summary>
public class EnviadorNoConfigurado : IEnviadorCorreo
{
    private readonly ILogger<EnviadorNoConfigurado> _logger;
    public EnviadorNoConfigurado(ILogger<EnviadorNoConfigurado> logger) => _logger = logger;

    public Task EnviarAsync(string para, string asunto, string html, CancellationToken ct = default)
    {
        _logger.LogError("No se envió el correo \"{Asunto}\" a {Para}: falta configurar Correo:Smtp:Host.", asunto, para);
        return Task.CompletedTask;
    }
}

/// <summary>Plantillas HTML de correo. Los clientes de correo exigen estilos en línea; los colores son los de la paleta.</summary>
public class PlantillasCorreo
{
    private const string Fondo = "#0D0B13", Superficie = "#1B1727", Texto = "#F6F4FB", Texto2 = "#B7AFD0", Primario = "#7B5CE5", Acento = "#F9B26A";
    private readonly HtmlEncoder _html = HtmlEncoder.Default;

    public (string Asunto, string Html) RecuperarContrasena(string nombre, string enlace) => (
        "Restablece tu contraseña de LlamitasStore",
        Envolver($"""
            <h1 style="margin:0 0 16px;font-size:22px;color:{Texto}">Hola, {_html.Encode(nombre)}</h1>
            <p style="color:{Texto2};line-height:1.6">Recibimos una solicitud para restablecer la contraseña de tu cuenta.
            El enlace vence en 3 horas y solo funciona una vez.</p>
            <p style="margin:28px 0"><a href="{_html.Encode(enlace)}" style="background:{Primario};color:#FFFFFF;padding:14px 28px;border-radius:999px;text-decoration:none;font-weight:700;letter-spacing:.06em;text-transform:uppercase;font-size:13px">Restablecer contraseña</a></p>
            <p style="color:{Texto2};font-size:13px;line-height:1.6">Si no fuiste tú, ignora este mensaje: tu contraseña no cambiará.</p>
            """));

    public (string Asunto, string Html) Bienvenida(string nombre, string urlTienda) => (
        "¡Bienvenido a LlamitasStore!",
        Envolver($"""
            <h1 style="margin:0 0 16px;font-size:22px;color:{Texto}">¡Hola, {_html.Encode(nombre)}!</h1>
            <p style="color:{Texto2};line-height:1.6">Tu cuenta fue creada. Ya puedes guardar favoritos y direcciones de envío.</p>
            <p style="margin:28px 0"><a href="{_html.Encode(urlTienda)}" style="background:{Primario};color:#FFFFFF;padding:14px 28px;border-radius:999px;text-decoration:none;font-weight:700;letter-spacing:.06em;text-transform:uppercase;font-size:13px">Ir a la tienda</a></p>
            """));

    private static string Envolver(string cuerpo) => $"""
        <!DOCTYPE html>
        <html lang="es"><body style="margin:0;background:{Fondo};font-family:Outfit,Segoe UI,Arial,sans-serif">
        <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:{Fondo};padding:32px 12px">
          <tr><td align="center">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:560px;background:{Superficie};border-radius:20px;padding:32px">
              <tr><td>
                <p style="margin:0 0 24px;font-weight:800;letter-spacing:.12em;color:{Acento};font-size:14px">LLAMITASSTORE</p>
                {cuerpo}
              </td></tr>
            </table>
          </td></tr>
        </table>
        </body></html>
        """;
}
