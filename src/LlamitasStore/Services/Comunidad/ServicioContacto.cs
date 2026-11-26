using System.Net;
using LlamitasStore.Data;
using LlamitasStore.Models.Entities;
using LlamitasStore.Services.Correo;
using LlamitasStore.ViewModels;

namespace LlamitasStore.Services.Comunidad;

/// <summary>
/// Formulario de contacto: el mensaje se guarda en la bandeja del panel (Mensajes) y, si la tienda tiene
/// correo configurado, se avisa por correo. Si el correo falla, el mensaje igual queda guardado.
/// </summary>
public class ServicioContacto
{
    private readonly AppDbContext _db;
    private readonly AjustesTienda _tienda;
    private readonly IEnviadorCorreo _correo;
    private readonly TimeProvider _reloj;
    private readonly ILogger<ServicioContacto> _log;

    public ServicioContacto(AppDbContext db, AjustesTienda tienda, IEnviadorCorreo correo, TimeProvider reloj, ILogger<ServicioContacto> log)
    {
        _db = db;
        _tienda = tienda;
        _correo = correo;
        _reloj = reloj;
        _log = log;
    }

    public async Task<int> GuardarAsync(ContactoVm vm, CancellationToken ct = default)
    {
        var asunto = vm.Asunto.Trim();
        if (!string.IsNullOrWhiteSpace(vm.NumeroPedido)) asunto = $"{asunto} · Pedido {vm.NumeroPedido.Trim().ToUpperInvariant()}";
        var m = new MensajeContacto
        {
            Nombre = vm.Nombre.Trim(),
            Email = vm.Email.Trim(),
            Asunto = asunto.Length > 150 ? asunto[..150] : asunto,
            Mensaje = vm.Mensaje.Trim(),
            Fecha = _reloj.GetUtcNow().UtcDateTime
        };
        _db.MensajesContacto.Add(m);
        await _db.SaveChangesAsync(ct);

        var destino = _tienda.Obtener(ClavesAjuste.EmailContacto);
        if (!string.IsNullOrWhiteSpace(destino))
        {
            try
            {
                Func<string, string> e = s => WebUtility.HtmlEncode(s);
                await _correo.EnviarAsync(destino, $"Nuevo mensaje de contacto: {m.Asunto}",
                    $"<p><strong>{e(m.Nombre)}</strong> ({e(m.Email)}) escribió:</p><p>{e(m.Mensaje).Replace("\n", "<br>")}</p>" +
                    "<p>Respóndelo desde el panel: Mensajes.</p>", ct);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "No se pudo avisar por correo del mensaje de contacto {Id}", m.Id);
            }
        }
        return m.Id;
    }
}
