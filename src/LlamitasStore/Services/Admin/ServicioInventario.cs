using LlamitasStore.Data;
using LlamitasStore.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace LlamitasStore.Services.Admin;

public record LineaOrdenCompra(int ProductoId, int Cantidad, decimal CostoUnitario);

/// <summary>
/// Movimientos de stock hechos por el personal. Producto.Stock es la única fuente de verdad y
/// cada cambio deja un MovimientoInventario con el stock resultante y quién lo hizo.
/// </summary>
public class ServicioInventario
{
    private readonly AppDbContext _db;
    private readonly TimeProvider _reloj;

    public ServicioInventario(AppDbContext db, TimeProvider reloj)
    {
        _db = db;
        _reloj = reloj;
    }

    /// <summary>
    /// Entrada (+), Salida (−) o Ajuste (fija el stock contado). Devuelve null si va bien o el motivo del rechazo.
    /// Las salidas usan UPDATE condicional: nunca dejan el stock negativo aunque haya ventas simultáneas.
    /// </summary>
    public async Task<string?> AjustarAsync(int productoId, TipoMovimiento tipo, int cantidad, string motivo, int usuarioId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(motivo)) return "Escribe el motivo del movimiento.";
        if (tipo is not (TipoMovimiento.Entrada or TipoMovimiento.Salida or TipoMovimiento.Ajuste)) return "Tipo de movimiento no válido.";
        if (tipo != TipoMovimiento.Ajuste && cantidad <= 0) return "La cantidad debe ser mayor a cero.";
        if (tipo == TipoMovimiento.Ajuste && cantidad < 0) return "El stock contado no puede ser negativo.";

        return await _db.EnTransaccionAsync<string?>(async c =>
        {
            var actual = await _db.Productos.Where(p => p.Id == productoId).Select(p => (int?)p.Stock).FirstOrDefaultAsync(c);
            if (actual is null) return "El producto no existe.";

            int delta, filas;
            switch (tipo)
            {
                case TipoMovimiento.Entrada:
                    delta = cantidad;
                    filas = await _db.Productos.Where(p => p.Id == productoId)
                        .ExecuteUpdateAsync(s => s.SetProperty(p => p.Stock, p => p.Stock + cantidad), c);
                    break;
                case TipoMovimiento.Salida:
                    delta = -cantidad;
                    filas = await _db.Productos.Where(p => p.Id == productoId && p.Stock >= cantidad)
                        .ExecuteUpdateAsync(s => s.SetProperty(p => p.Stock, p => p.Stock - cantidad), c);
                    if (filas == 0) return $"No hay stock suficiente: quedan {actual} unidades.";
                    break;
                default: // Ajuste por conteo físico
                    delta = cantidad - actual.Value;
                    if (delta == 0) return "El stock contado es igual al registrado: no hay nada que ajustar.";
                    filas = await _db.Productos.Where(p => p.Id == productoId && p.Stock == actual)
                        .ExecuteUpdateAsync(s => s.SetProperty(p => p.Stock, cantidad), c);
                    if (filas == 0) return "El stock cambió mientras ajustabas (hubo una venta). Vuelve a contar.";
                    break;
            }

            var resultante = await _db.Productos.Where(p => p.Id == productoId).Select(p => p.Stock).FirstAsync(c);
            _db.MovimientosInventario.Add(new MovimientoInventario
            {
                ProductoId = productoId, Tipo = tipo, Cantidad = delta, StockResultante = resultante,
                Motivo = motivo.Trim(), UsuarioId = usuarioId, Fecha = _reloj.GetUtcNow().UtcDateTime
            });
            await _db.SaveChangesAsync(c);
            return null;
        }, ct);
    }

    public async Task<(OrdenCompra? Orden, string? Error)> CrearOrdenCompraAsync(int proveedorId, IReadOnlyList<LineaOrdenCompra> lineas, string? notas, int usuarioId, CancellationToken ct = default)
    {
        var validas = lineas.Where(l => l.ProductoId > 0 && l.Cantidad > 0 && l.CostoUnitario >= 0).ToList();
        if (validas.Count == 0) return (null, "Agrega al menos un producto con cantidad mayor a cero.");
        if (validas.Select(l => l.ProductoId).Distinct().Count() != validas.Count) return (null, "Hay productos repetidos: une sus cantidades en una sola línea.");
        if (!await _db.Proveedores.AnyAsync(p => p.Id == proveedorId && p.Activo, ct)) return (null, "Elige un proveedor activo.");
        var ids = validas.Select(l => l.ProductoId).ToList();
        if (await _db.Productos.CountAsync(p => ids.Contains(p.Id), ct) != ids.Count) return (null, "Algún producto ya no existe.");

        var ahora = _reloj.GetUtcNow().UtcDateTime;
        var orden = new OrdenCompra
        {
            Numero = "T" + Guid.NewGuid().ToString("N")[..28], ProveedorId = proveedorId, Fecha = ahora, Estado = EstadoOrdenCompra.Borrador,
            Notas = string.IsNullOrWhiteSpace(notas) ? null : notas.Trim(), UsuarioId = usuarioId,
            Total = validas.Sum(l => Math.Round(l.Cantidad * l.CostoUnitario, 2)),
            Detalles = validas.Select(l => new DetalleOrdenCompra { ProductoId = l.ProductoId, Cantidad = l.Cantidad, CostoUnitario = l.CostoUnitario }).ToList()
        };
        _db.OrdenesCompra.Add(orden);
        await _db.SaveChangesAsync(ct);
        orden.Numero = $"OC-{ahora:yyyyMMdd}-{orden.Id:D4}";
        await _db.SaveChangesAsync(ct);
        return (orden, null);
    }

    /// <summary>Recibir la mercadería: suma stock de cada línea con su movimiento de Entrada. Solo desde Borrador.</summary>
    public Task<string?> RecibirOrdenCompraAsync(int ordenId, int usuarioId, CancellationToken ct = default) =>
        _db.EnTransaccionAsync<string?>(async c =>
        {
            var orden = await _db.OrdenesCompra.Include(o => o.Detalles).FirstOrDefaultAsync(o => o.Id == ordenId, c);
            if (orden is null) return "La orden no existe.";
            if (orden.Estado != EstadoOrdenCompra.Borrador) return "Solo se pueden recibir órdenes en borrador.";

            var ahora = _reloj.GetUtcNow().UtcDateTime;
            foreach (var d in orden.Detalles)
            {
                await _db.Productos.Where(p => p.Id == d.ProductoId)
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.Stock, p => p.Stock + d.Cantidad), c);
                var stock = await _db.Productos.Where(p => p.Id == d.ProductoId).Select(p => p.Stock).FirstAsync(c);
                _db.MovimientosInventario.Add(new MovimientoInventario
                {
                    ProductoId = d.ProductoId, Tipo = TipoMovimiento.Entrada, Cantidad = d.Cantidad, StockResultante = stock,
                    Motivo = $"Recepción {orden.Numero}", OrdenCompraId = orden.Id, UsuarioId = usuarioId, Fecha = ahora
                });
            }
            orden.Estado = EstadoOrdenCompra.Recibida;
            orden.FechaRecepcion = ahora;
            await _db.SaveChangesAsync(c);
            return null;
        }, ct);

    public async Task<string?> CancelarOrdenCompraAsync(int ordenId, CancellationToken ct = default)
    {
        var orden = await _db.OrdenesCompra.FirstOrDefaultAsync(o => o.Id == ordenId, ct);
        if (orden is null) return "La orden no existe.";
        if (orden.Estado != EstadoOrdenCompra.Borrador) return "Solo se pueden cancelar órdenes en borrador (una orden recibida ya sumó stock).";
        orden.Estado = EstadoOrdenCompra.Cancelada;
        await _db.SaveChangesAsync(ct);
        return null;
    }
}
