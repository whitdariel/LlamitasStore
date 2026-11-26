using LlamitasStore.Data;
using LlamitasStore.Models.Entities;
using LlamitasStore.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace LlamitasStore.Services.Catalogo;

/// <summary>
/// Secciones de la página de inicio, siempre con datos reales:
/// una sección sin productos (p. ej. "Más vendidos" antes de la primera venta) se oculta, no se rellena.
/// </summary>
public class ServicioInicio
{
    private const int PorSeccion = 8;

    private readonly AppDbContext _db;
    private readonly ServicioCatalogo _catalogo;
    private readonly ServicioPrecios _precios;

    public ServicioInicio(AppDbContext db, ServicioCatalogo catalogo, ServicioPrecios precios)
    {
        _db = db;
        _catalogo = catalogo;
        _precios = precios;
    }

    public async Task<InicioViewModel> ObtenerAsync(CancellationToken ct = default)
    {
        var promociones = await _precios.ObtenerPromocionesVigentesAsync(ct);
        var activos = _db.Productos.AsNoTracking()
            .Where(p => p.Estado == EstadoProducto.Activo && p.Categoria.Activa && p.Marca.Activa);

        // Ofertas: mayor porcentaje de descuento real (oferta o promoción) entre los que tienen stock.
        var candidatos = await activos.Where(p => p.Stock > 0)
            .Select(p => new { p.Id, p.Precio, p.PrecioOferta, p.MarcaId, p.CategoriaId })
            .ToListAsync(ct);
        var idsOfertas = candidatos
            .Select(p => (p.Id, Precio: ServicioPrecios.Calcular(p.Precio, p.PrecioOferta, p.Id, p.MarcaId, p.CategoriaId, promociones)))
            .Where(x => x.Precio.TieneDescuento)
            .OrderByDescending(x => x.Precio.PorcentajeDescuento).ThenBy(x => x.Id)
            .Take(PorSeccion).Select(x => x.Id).ToList();

        var idsDestacados = await activos.Where(p => p.Destacado && p.Stock > 0)
            .OrderByDescending(p => p.TotalVendidos).ThenBy(p => p.Id)
            .Select(p => p.Id).Take(PorSeccion).ToListAsync(ct);

        var idsNovedades = await activos.Where(p => p.Stock > 0)
            .OrderByDescending(p => p.EsNuevo).ThenByDescending(p => p.FechaCreacion).ThenByDescending(p => p.Id)
            .Select(p => p.Id).Take(PorSeccion).ToListAsync(ct);

        var idsVendidos = await activos.Where(p => p.TotalVendidos > 0)
            .OrderByDescending(p => p.TotalVendidos).ThenBy(p => p.Id)
            .Select(p => p.Id).Take(PorSeccion).ToListAsync(ct);

        var marcas = await _db.Marcas.AsNoTracking()
            .Where(m => m.Activa && m.Destacada && m.Productos.Any(p => p.Estado == EstadoProducto.Activo))
            .OrderBy(m => m.Nombre)
            .Select(m => new MarcaResumen(m.Nombre, m.Slug, m.Productos.Count(p => p.Estado == EstadoProducto.Activo)))
            .ToListAsync(ct);

        var categorias = await _db.Categorias.AsNoTracking()
            .Where(c => c.Activa)
            .OrderBy(c => c.Orden)
            .Select(c => new CategoriaResumen(c.Nombre, c.Slug, c.Icono, c.Productos.Count(p => p.Estado == EstadoProducto.Activo)))
            .ToListAsync(ct);

        var totalProductos = await activos.CountAsync(ct);
        var totalMarcas = await _db.Marcas.CountAsync(m => m.Activa && m.Productos.Any(p => p.Estado == EstadoProducto.Activo), ct);

        return new InicioViewModel(
            categorias,
            await _catalogo.ObtenerTarjetasAsync(idsOfertas, promociones, ct),
            await _catalogo.ObtenerTarjetasAsync(idsDestacados, promociones, ct),
            await _catalogo.ObtenerTarjetasAsync(idsNovedades, promociones, ct),
            await _catalogo.ObtenerTarjetasAsync(idsVendidos, promociones, ct),
            marcas,
            totalProductos,
            totalMarcas);
    }
}
