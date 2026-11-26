using LlamitasStore.Data;
using Microsoft.EntityFrameworkCore;

namespace LlamitasStore.Services.Catalogo;

public record PrecioCalculado(decimal PrecioLista, decimal PrecioFinal, string? Motivo)
{
    public bool TieneDescuento => PrecioFinal < PrecioLista;
    public decimal Ahorro => PrecioLista - PrecioFinal;
    public int PorcentajeDescuento =>
        PrecioLista <= 0 || !TieneDescuento ? 0 : (int)Math.Round((1 - PrecioFinal / PrecioLista) * 100, MidpointRounding.AwayFromZero);
}

public record PromocionVigente(int Id, string Nombre, decimal Porcentaje, int? ProductoId, int? MarcaId, int? CategoriaId)
{
    public bool Aplica(int productoId, int marcaId, int categoriaId) =>
        ProductoId == productoId || MarcaId == marcaId || CategoriaId == categoriaId;
}

/// <summary>
/// Única fuente del precio de venta. Regla: el cliente paga el MENOR entre
/// precio de lista, precio de oferta del producto y precio con la mejor promoción vigente.
/// Los descuentos no se acumulan entre sí (los cupones se aplican luego, sobre el subtotal).
/// </summary>
public class ServicioPrecios
{
    private readonly AppDbContext _db;
    private readonly TimeProvider _reloj;

    public ServicioPrecios(AppDbContext db, TimeProvider reloj)
    {
        _db = db;
        _reloj = reloj;
    }

    public async Task<IReadOnlyList<PromocionVigente>> ObtenerPromocionesVigentesAsync(CancellationToken ct = default)
    {
        var ahora = _reloj.GetUtcNow().UtcDateTime;
        return await _db.Promociones.AsNoTracking()
            .Where(p => p.Activa && p.FechaInicio <= ahora && p.FechaFin > ahora)
            .Select(p => new PromocionVigente(p.Id, p.Nombre, p.Porcentaje, p.ProductoId, p.MarcaId, p.CategoriaId))
            .ToListAsync(ct);
    }

    public static PrecioCalculado Calcular(decimal precio, decimal? precioOferta, int productoId, int marcaId, int categoriaId,
        IEnumerable<PromocionVigente> promociones)
    {
        var mejor = precio;
        string? motivo = null;

        if (precioOferta is > 0 && precioOferta < mejor)
        {
            mejor = precioOferta.Value;
            motivo = "Oferta";
        }

        foreach (var promo in promociones)
        {
            if (!promo.Aplica(productoId, marcaId, categoriaId) || promo.Porcentaje <= 0) continue;

            var conPromo = Math.Round(precio * (1 - promo.Porcentaje / 100m), 2, MidpointRounding.AwayFromZero);
            if (conPromo < mejor)
            {
                mejor = conPromo;
                motivo = promo.Nombre;
            }
        }

        return new PrecioCalculado(precio, mejor, motivo);
    }
}
