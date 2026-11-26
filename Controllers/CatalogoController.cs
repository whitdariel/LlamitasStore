using LlamitasStore.Infraestructura;
using LlamitasStore.Services;
using LlamitasStore.Services.Catalogo;
using LlamitasStore.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LlamitasStore.Controllers;

public class CatalogoController : Controller
{
    private readonly ServicioCatalogo _catalogo;
    private readonly AjustesTienda _tienda;

    public CatalogoController(ServicioCatalogo catalogo, AjustesTienda tienda)
    {
        _catalogo = catalogo;
        _tienda = tienda;
    }

    [HttpGet("catalogo/{categoria?}")]
    public async Task<IActionResult> Index(string? categoria, [FromQuery] FiltroCatalogo filtro, CancellationToken ct)
    {
        filtro.Categoria = categoria;
        var resultado = await _catalogo.BuscarAsync(filtro, ct);
        if (resultado is null) return NotFound();

        return View(resultado);
    }

    [HttpGet("producto/{slug}")]
    public async Task<IActionResult> Producto(string slug, [FromServices] Services.Comunidad.ServicioResenas resenas, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(slug) || slug.Length > 180) return NotFound();

        var ficha = await _catalogo.ObtenerFichaAsync(slug, ct);
        if (ficha is null) return NotFound();

        // Reseña propia para precargar el formulario (el id sale de la sesión, nunca del navegador).
        if (int.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var usuarioId))
            ViewData["ResenaPropia"] = await resenas.ObtenerPropiaAsync(usuarioId, ficha.Producto.Id, ct);

        return View(ficha);
    }

    /// <summary>JSON para el buscador del header. Los textos se insertan con textContent en el cliente.</summary>
    [HttpGet("catalogo/sugerencias")]
    [EnableRateLimiting(PoliticasLimite.Busqueda)]
    [ResponseCache(Duration = 30, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> Sugerencias(string? q, CancellationToken ct)
    {
        var s = await _catalogo.SugerirAsync(q, ct: ct);
        return Json(new
        {
            categorias = s.Categorias.Select(c => new { nombre = c.Nombre, url = $"/catalogo/{c.Slug}" }),
            productos = s.Productos.Select(p => new
            {
                nombre = p.Nombre,
                url = $"/producto/{p.Slug}",
                marca = p.Marca,
                categoria = p.Categoria,
                imagen = p.ImagenUrl,
                precio = _tienda.Dinero(p.Precio.PrecioFinal)
            })
        });
    }
}
