using LlamitasStore.Services.Catalogo;
using Microsoft.AspNetCore.Mvc;

namespace LlamitasStore.Controllers;

public class HomeController : Controller
{
    private readonly ServicioInicio _inicio;

    public HomeController(ServicioInicio inicio) => _inicio = inicio;

    public async Task<IActionResult> Index(CancellationToken ct) => View(await _inicio.ObtenerAsync(ct));

    /// <summary>La política de privacidad se movió junto a las demás políticas; se conservan los enlaces viejos.</summary>
    public IActionResult Privacy() => RedirectPermanent("/politicas/privacidad");
}
