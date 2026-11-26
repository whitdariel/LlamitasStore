using LlamitasStore.Areas.Admin.Models;
using LlamitasStore.Data;
using LlamitasStore.Infraestructura;
using LlamitasStore.Models.Entities;
using LlamitasStore.Services;
using LlamitasStore.Services.Admin;
using LlamitasStore.Services.Correo;
using LlamitasStore.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LlamitasStore.Areas.Admin.Controllers;

[Route("admin/pedidos")]
public class PedidosController : AdminController
{
    private readonly AppDbContext _db;
    private readonly ServicioAdminPedidos _pedidos;
    private readonly IEnviadorCorreo _correo;
    private readonly PlantillasCorreo _plantillas;
    private readonly ILogger<PedidosController> _logger;

    public PedidosController(AppDbContext db, ServicioAdminPedidos pedidos, IEnviadorCorreo correo, PlantillasCorreo plantillas, ILogger<PedidosController> logger)
    {
        _db = db;
        _pedidos = pedidos;
        _correo = correo;
        _plantillas = plantillas;
        _logger = logger;
    }

    public record FilaPedido(string Numero, DateTime Fecha, string Cliente, string Email, EstadoPedido Estado, MetodoPago Metodo, EstadoPago? Pago, decimal Total, int Items);

    [HttpGet("")]
    [Permiso(Modulos.Pedidos)]
    public async Task<IActionResult> Index(string? q, EstadoPedido? estado, int pagina = 1, CancellationToken ct = default)
    {
        var consulta = _db.Pedidos.AsNoTracking();
        if (estado is not null) consulta = consulta.Where(p => p.Estado == estado);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var t = $"%{q.Trim()}%";
            consulta = consulta.Where(p => EF.Functions.Like(p.Numero, t) || EF.Functions.Like(p.Usuario.Email!, t)
                || EF.Functions.Like(p.Usuario.Nombre + " " + p.Usuario.Apellido, t));
        }
        var (actual, saltar) = Paginar(pagina);
        var total = await consulta.CountAsync(ct);
        var filas = await consulta.OrderByDescending(p => p.Fecha).Skip(saltar).Take(TamanoPagina)
            .Select(p => new FilaPedido(p.Numero, p.Fecha, p.Usuario.Nombre + " " + p.Usuario.Apellido, p.Usuario.Email!, p.Estado, p.MetodoPago,
                p.Pagos.OrderByDescending(x => x.Fecha).Select(x => (EstadoPago?)x.Estado).FirstOrDefault(), p.Total, p.Detalles.Sum(d => d.Cantidad)))
            .ToListAsync(ct);
        (ViewData["Q"], ViewData["Estado"]) = (q, estado);
        ViewData["Conteo"] = await _db.Pedidos.GroupBy(p => p.Estado).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
        return View(new Pagina<FilaPedido>(filas, total, actual));
    }

    [HttpGet("{numero}")]
    [Permiso(Modulos.Pedidos)]
    public async Task<IActionResult> Detalle(string numero, CancellationToken ct)
    {
        var p = await _pedidos.ObtenerAsync(numero, ct);
        return p is null ? NotFound() : View(p);
    }

    [HttpPost("{numero}/estado")]
    [Permiso(Modulos.Pedidos, P.Editar)]
    public async Task<IActionResult> CambiarEstado(string numero, EstadoPedido estado, string? comentario, CancellationToken ct)
    {
        var error = await _pedidos.CambiarEstadoAsync(numero, estado, comentario, UsuarioId, ct);
        if (error is null) await AvisarClienteAsync(numero, $"Tu pedido {numero.ToUpperInvariant()}: {TextosPedido.Estado(estado)}",
            comentario ?? ServicioAdminPedidos.MensajeEstado(estado), ct);
        return Resultado(error, $"Pedido actualizado a «{TextosPedido.Estado(estado)}». Se notificó al cliente.", nameof(Detalle), new { numero });
    }

    [HttpPost("{numero}/pago")]
    [Permiso(Modulos.Pedidos, P.Editar)]
    public async Task<IActionResult> ConfirmarPago(string numero, string? referencia, CancellationToken ct)
    {
        var error = await _pedidos.ConfirmarPagoAsync(numero, referencia, UsuarioId, ct);
        if (error is null) await AvisarClienteAsync(numero, $"Pago confirmado: {numero.ToUpperInvariant()}", "Recibimos tu pago. ¡Gracias!", ct);
        return Resultado(error, "Pago confirmado.", nameof(Detalle), new { numero });
    }

    /// <summary>Correo al cliente (además de la notificación en su cuenta). Si falla, el cambio ya quedó guardado.</summary>
    private async Task AvisarClienteAsync(string numero, string asunto, string mensaje, CancellationToken ct)
    {
        try
        {
            var p = await _pedidos.ObtenerAsync(numero, ct);
            if (p?.Usuario.Email is null) return;
            var enlace = Url.Action("Pedido", "MiCuenta", new { area = "", numero = p.Numero }, Request.Scheme)!;
            var html = _plantillas.Aviso(p.Usuario.Nombre, mensaje, enlace, $"Ver el pedido {p.Numero}");
            await _correo.EnviarAsync(p.Usuario.Email, asunto, html, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo enviar el aviso del pedido {Numero}", numero);
        }
    }
}
