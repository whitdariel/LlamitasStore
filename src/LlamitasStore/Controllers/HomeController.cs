using LlamitasStore.Data;
using LlamitasStore.Models.Entities;
using LlamitasStore.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LlamitasStore.Controllers;

public class HomeController : Controller
{
    private readonly AppDbContext _db;

    public HomeController(AppDbContext db) => _db = db;

    public async Task<IActionResult> Index()
    {
        var categorias = await _db.Categorias.AsNoTracking()
            .Where(c => c.Activa)
            .OrderBy(c => c.Orden)
            .Select(c => new CategoriaResumen(c.Nombre, c.Slug, c.Icono,
                c.Productos.Count(p => p.Estado == EstadoProducto.Activo)))
            .ToListAsync();

        return View(new InicioViewModel(categorias));
    }

    public IActionResult Privacy() => View();
}
