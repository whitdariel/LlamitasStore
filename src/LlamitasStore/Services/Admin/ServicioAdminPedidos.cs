using LlamitasStore.Data;
using LlamitasStore.Models.Entities;
using LlamitasStore.Services.Ventas;
using LlamitasStore.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace LlamitasStore.Services.Admin;

/// <summary>Gestión de pedidos por el personal: transiciones de estado válidas y confirmación de pagos.</summary>
public class ServicioAdminPedidos
{
    /// <summary>Flujo permitido. Entregado y Cancelado son finales.</summary>
    public static readonly IReadOnlyDictionary<EstadoPedido, EstadoPedido[]> Transiciones = new Dictionary<EstadoPedido, EstadoPedido[]>
    {
        [EstadoPedido.Pendiente] = [EstadoPedido.Confirmado, EstadoPedido.Cancelado],
        [EstadoPedido.Confirmado] = [EstadoPedido.EnPreparacion, EstadoPedido.Cancelado],
        [EstadoPedido.EnPreparacion] = [EstadoPedido.Enviado, EstadoPedido.Cancelado],
        [EstadoPedido.Enviado] = [EstadoPedido.Entregado],
        [EstadoPedido.Entregado] = [],
        [EstadoPedido.Cancelado] = []
    };

    private readonly AppDbContext _db;
    private readonly ServicioPedidos _pedidos;
    private readonly TimeProvider _reloj;

    public ServicioAdminPedidos(AppDbContext db, ServicioPedidos pedidos, TimeProvider reloj)
    {
        _db = db;
        _pedidos = pedidos;
        _reloj = reloj;
    }

    public static bool PuedeCambiar(EstadoPedido desde, EstadoPedido hacia) => Transiciones[desde].Contains(hacia);

    public Task<Pedido?> ObtenerAsync(string numero, CancellationToken ct = default)
    {
        numero = ServicioPedidos.NormalizarNumero(numero);
        return _db.Pedidos.AsNoTracking()
            .Include(p => p.Usuario)
            .Include(p => p.Detalles).ThenInclude(d => d.Producto)
            .Include(p => p.Historial).ThenInclude(h => h.Usuario)
            .Include(p => p.Pagos)
            .AsSplitQuery()
            .FirstOrDefaultAsync(p => p.Numero == numero, ct);
    }

    /// <summary>Cambia el estado si la transición es válida. Cancelar repone stock y libera el cupón. Notifica al cliente.</summary>
    public async Task<string?> CambiarEstadoAsync(string numero, EstadoPedido nuevo, string? comentario, int usuarioId, CancellationToken ct = default)
    {
        numero = ServicioPedidos.NormalizarNumero(numero);
        try
        {
            return await _db.EnTransaccionAsync<string?>(async c =>
            {
                var pedido = await _db.Pedidos.Include(p => p.Detalles).Include(p => p.Pagos).FirstOrDefaultAsync(p => p.Numero == numero, c);
                if (pedido is null) return "El pedido no existe.";
                if (!PuedeCambiar(pedido.Estado, nuevo))
                    return $"No se puede pasar de «{TextosPedido.Estado(pedido.Estado)}» a «{TextosPedido.Estado(nuevo)}».";
                // Las transferencias se confirman solo con el pago verificado.
                if (nuevo == EstadoPedido.Confirmado && pedido.MetodoPago == MetodoPago.Transferencia && pedido.Pagos.All(p => p.Estado != EstadoPago.Confirmado))
                    return "Primero confirma el pago de la transferencia.";

                var ahora = _reloj.GetUtcNow().UtcDateTime;
                if (nuevo == EstadoPedido.Cancelado)
                {
                    await _pedidos.DevolverStockAsync(pedido, usuarioId, "Cancelado por la tienda", c);
                    foreach (var pago in pedido.Pagos.Where(p => p.Estado == EstadoPago.Pendiente)) pago.Estado = EstadoPago.Rechazado;
                    foreach (var pago in pedido.Pagos.Where(p => p.Estado == EstadoPago.Confirmado)) pago.Estado = EstadoPago.Reembolsado;
                }
                // Contra entrega: al entregarse se cobra.
                if (nuevo == EstadoPedido.Entregado)
                {
                    foreach (var pago in pedido.Pagos.Where(p => p.Estado == EstadoPago.Pendiente && p.Metodo == MetodoPago.ContraEntrega))
                    {
                        pago.Estado = EstadoPago.Confirmado;
                        pago.FechaConfirmacion = ahora;
                        pago.ConfirmadoPorId = usuarioId;
                    }
                }

                pedido.Estado = nuevo;
                pedido.FechaActualizacion = ahora;
                var texto = string.IsNullOrWhiteSpace(comentario) ? null : comentario.Trim();
                _db.PedidoHistorial.Add(new PedidoEstadoHistorial { PedidoId = pedido.Id, Estado = nuevo, Comentario = texto, UsuarioId = usuarioId, Fecha = ahora });
                _db.Notificaciones.Add(new Notificacion
                {
                    UsuarioId = pedido.UsuarioId, Titulo = $"Tu pedido {pedido.Numero}: {TextosPedido.Estado(nuevo)}",
                    Mensaje = texto ?? MensajeEstado(nuevo), Url = $"/cuenta/pedidos/{pedido.Numero}", Fecha = ahora
                });
                await _db.SaveChangesAsync(c);
                return null;
            }, ct);
        }
        catch (ErrorPedido e)
        {
            _db.ChangeTracker.Clear();
            return e.Message;
        }
    }

    public async Task<string?> ConfirmarPagoAsync(string numero, string? referencia, int usuarioId, CancellationToken ct = default)
    {
        numero = ServicioPedidos.NormalizarNumero(numero);
        var pedido = await _db.Pedidos.Include(p => p.Pagos).FirstOrDefaultAsync(p => p.Numero == numero, ct);
        if (pedido is null) return "El pedido no existe.";
        if (pedido.Estado == EstadoPedido.Cancelado) return "El pedido está cancelado.";
        var pago = pedido.Pagos.Where(p => p.Estado == EstadoPago.Pendiente).OrderByDescending(p => p.Fecha).FirstOrDefault();
        if (pago is null) return "No hay pagos pendientes en este pedido.";

        var ahora = _reloj.GetUtcNow().UtcDateTime;
        pago.Estado = EstadoPago.Confirmado;
        pago.FechaConfirmacion = ahora;
        pago.ConfirmadoPorId = usuarioId;
        if (!string.IsNullOrWhiteSpace(referencia)) pago.Referencia = referencia.Trim();
        _db.Notificaciones.Add(new Notificacion
        {
            UsuarioId = pedido.UsuarioId, Titulo = $"Pago confirmado: {pedido.Numero}",
            Mensaje = "Recibimos tu pago. ¡Gracias!", Url = $"/cuenta/pedidos/{pedido.Numero}", Fecha = ahora
        });
        await _db.SaveChangesAsync(ct);
        return null;
    }

    public static string MensajeEstado(EstadoPedido e) => e switch
    {
        EstadoPedido.Confirmado => "Confirmamos tu pedido y lo prepararemos pronto.",
        EstadoPedido.EnPreparacion => "Estamos preparando tu pedido.",
        EstadoPedido.Enviado => "Tu pedido va en camino.",
        EstadoPedido.Entregado => "Tu pedido fue entregado. ¡Que lo disfrutes!",
        EstadoPedido.Cancelado => "Tu pedido fue cancelado.",
        _ => "Tu pedido cambió de estado."
    };
}
