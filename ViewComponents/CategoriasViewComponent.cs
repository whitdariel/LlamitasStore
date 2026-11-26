using LlamitasStore.Data;
using LlamitasStore.Models.Entities;
using LlamitasStore.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace LlamitasStore.ViewComponents;

/// <summary>Categorías para el megamenú y el footer (caché de 5 minutos: se muestran en todas las páginas).</summary>
public class CategoriasViewComponent : ViewComponent
{
    public const string ClaveCache = "menu-categorias";

    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;

    public CategoriasViewComponent(AppDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    /// <param name="vista">"Megamenu", "Movil" o "Footer".</param>
    public async Task<IViewComponentResult> InvokeAsync(string vista)
    {
        var categorias = await _cache.GetOrCreateAsync(ClaveCache, async e =>
        {
            e.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
            return await _db.Categorias.AsNoTracking()
                .Where(c => c.Activa)
                .OrderBy(c => c.Orden)
                .Select(c => new CategoriaResumen(c.Nombre, c.Slug, c.Icono, c.Productos.Count(p => p.Estado == EstadoProducto.Activo)))
                .ToListAsync();
        });

        return View(vista, categorias);
    }
}
