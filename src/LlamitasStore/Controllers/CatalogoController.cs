using LlamitasStore.Services.Catalogo;
using LlamitasStore.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace LlamitasStore.Controllers;

public class CatalogoController : Controller
{
    private readonly ServicioCatalogo _catalogo;

    public CatalogoController(ServicioCatalogo catalogo) => _catalogo = catalogo;

    [HttpGet("catalogo/{categoria?}")]
    public async Task<IActionResult> Index(string? categoria, [FromQuery] FiltroCatalogo filtro, CancellationToken ct)
    {
        filtro.Categoria = categoria;
        var resultado = await _catalogo.BuscarAsync(filtro, ct);
        if (resultado is null) return NotFound();

        return View(resultado);
    }

    [HttpGet("producto/{slug}")]
    public async Task<IActionResult> Producto(string slug, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(slug) || slug.Length > 180) return NotFound();

        var ficha = await _catalogo.ObtenerFichaAsync(slug, ct);
        if (ficha is null) return NotFound();

        return View(ficha);
    }
}
