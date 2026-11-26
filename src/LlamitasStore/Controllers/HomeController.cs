using LlamitasStore.Services.Catalogo;
using Microsoft.AspNetCore.Mvc;

namespace LlamitasStore.Controllers;

public class HomeController : Controller
{
    private readonly ServicioInicio _inicio;

    public HomeController(ServicioInicio inicio) => _inicio = inicio;

    public async Task<IActionResult> Index(CancellationToken ct) => View(await _inicio.ObtenerAsync(ct));

    public IActionResult Privacy() => View();
}
