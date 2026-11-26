using LlamitasStore.Data;
using LlamitasStore.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace LlamitasStore.Services.Admin;

public record PuntoVenta(DateOnly Dia, decimal Total, int Pedidos);
public record ProductoTop(string Nombre, string Slug, int Unidades, decimal Ingresos);
public record StockBajo(int Id, string Nombre, string Sku, int Stock, int Minimo);
public record PedidoReciente(string Numero, string Cliente, DateTime Fecha, EstadoPedido Estado, decimal Total);

public record DashboardVm(
    decimal VentasMes, decimal VentasMesAnterior, int PedidosMes, decimal TicketPromedio,
    int PendientesAtencion, int ProductosActivos, int ProductosStockBajo, int Clientes, int ClientesNuevosMes,
    IReadOnlyList<PuntoVenta> Ultimos30Dias, IReadOnlyList<ProductoTop> TopProductos,
    IReadOnlyList<StockBajo> StockBajo, IReadOnlyList<PedidoReciente> Recientes,
    IReadOnlyDictionary<EstadoPedido, int> PedidosPorEstado)
{
    public decimal? VariacionVentas => VentasMesAnterior == 0 ? null : Math.Round((VentasMes - VentasMesAnterior) / VentasMesAnterior * 100, 1);
}

/// <summary>
/// Indicadores del panel. "Venta" = pedido no cancelado (se cuenta al crearse, sin esperar el pago).
/// Las fechas se agrupan en UTC−4 (hora de Bolivia).
/// </summary>
public class ServicioDashboard
{
    private static readonly TimeSpan HoraBolivia = TimeSpan.FromHours(-4);
    private readonly AppDbContext _db;
    private readonly TimeProvider _reloj;

    public ServicioDashboard(AppDbContext db, TimeProvider reloj)
    {
        _db = db;
        _reloj = reloj;
    }

    public async Task<DashboardVm> ObtenerAsync(CancellationToken ct = default)
    {
        var ahoraLocal = _reloj.GetUtcNow().ToOffset(HoraBolivia);
        var inicioMesLocal = new DateTime(ahoraLocal.Year, ahoraLocal.Month, 1);
        var inicioMes = inicioMesLocal - HoraBolivia;               // a UTC
        var inicioMesAnterior = inicioMesLocal.AddMonths(-1) - HoraBolivia;
        var hoyLocal = DateOnly.FromDateTime(ahoraLocal.DateTime);
        var desde30 = hoyLocal.AddDays(-29).ToDateTime(TimeOnly.MinValue) - HoraBolivia;

        var validos = _db.Pedidos.AsNoTracking().Where(p => p.Estado != EstadoPedido.Cancelado);

        var delMes = await validos.Where(p => p.Fecha >= inicioMes).Select(p => p.Total).ToListAsync(ct);
        var mesAnterior = await validos.Where(p => p.Fecha >= inicioMesAnterior && p.Fecha < inicioMes).SumAsync(p => (decimal?)p.Total, ct) ?? 0;

        // Serie diaria: se agrupa en memoria por día local (pocas filas: 30 días).
        var ventas30 = await validos.Where(p => p.Fecha >= desde30).Select(p => new { p.Fecha, p.Total }).ToListAsync(ct);
        var porDia = ventas30.GroupBy(v => DateOnly.FromDateTime(v.Fecha + HoraBolivia))
            .ToDictionary(g => g.Key, g => (Total: g.Sum(x => x.Total), Pedidos: g.Count()));
        var serie = Enumerable.Range(0, 30).Select(i =>
        {
            var dia = hoyLocal.AddDays(i - 29);
            var v = porDia.GetValueOrDefault(dia);
            return new PuntoVenta(dia, v.Total, v.Pedidos);
        }).ToList();

        // Se agrupa y ordena en SQL; el record se arma al final (EF no traduce ordenar sobre un constructor).
        var top = (await _db.DetallesPedido.AsNoTracking()
            .Where(d => d.Pedido.Estado != EstadoPedido.Cancelado && d.Pedido.Fecha >= desde30)
            .GroupBy(d => d.ProductoId)
            .Select(g => new { ProductoId = g.Key, Unidades = g.Sum(x => x.Cantidad), Ingresos = g.Sum(x => x.Subtotal) })
            .OrderByDescending(x => x.Unidades)
            .Take(5).ToListAsync(ct)).ToList();
        var nombres = await _db.Productos.AsNoTracking().Where(p => top.Select(t => t.ProductoId).Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => (p.Nombre, p.Slug), ct);
        var topProductos = top.Select(t => new ProductoTop(nombres[t.ProductoId].Nombre, nombres[t.ProductoId].Slug, t.Unidades, t.Ingresos))
            .OrderByDescending(t => t.Unidades).ThenByDescending(t => t.Ingresos).ToList();

        var stockBajo = await _db.Productos.AsNoTracking()
            .Where(p => p.Estado == EstadoProducto.Activo && p.Stock <= p.StockMinimo)
            .OrderBy(p => p.Stock).ThenBy(p => p.Nombre)
            .Select(p => new StockBajo(p.Id, p.Nombre, p.Sku, p.Stock, p.StockMinimo))
            .ToListAsync(ct);

        var recientes = await _db.Pedidos.AsNoTracking()
            .OrderByDescending(p => p.Fecha).Take(8)
            .Select(p => new PedidoReciente(p.Numero, p.Usuario.Nombre + " " + p.Usuario.Apellido, p.Fecha, p.Estado, p.Total))
            .ToListAsync(ct);

        var porEstado = await _db.Pedidos.AsNoTracking().GroupBy(p => p.Estado)
            .Select(g => new { g.Key, Cantidad = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Cantidad, ct);

        var idRolCliente = await _db.Roles.Where(r => r.Name == Roles.Cliente).Select(r => r.Id).FirstOrDefaultAsync(ct);
        var clientes = _db.Users.Where(u => _db.UserRoles.Any(ur => ur.UserId == u.Id && ur.RoleId == idRolCliente));

        return new DashboardVm(
            delMes.Sum(), mesAnterior, delMes.Count, delMes.Count == 0 ? 0 : Math.Round(delMes.Sum() / delMes.Count, 2),
            porEstado.GetValueOrDefault(EstadoPedido.Pendiente) + porEstado.GetValueOrDefault(EstadoPedido.Confirmado) + porEstado.GetValueOrDefault(EstadoPedido.EnPreparacion),
            await _db.Productos.CountAsync(p => p.Estado == EstadoProducto.Activo, ct), stockBajo.Count,
            await clientes.CountAsync(ct), await clientes.CountAsync(u => u.FechaCreacion >= inicioMes, ct),
            serie, topProductos, stockBajo.Take(8).ToList(), recientes, porEstado);
    }
}
