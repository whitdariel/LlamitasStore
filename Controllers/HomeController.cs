using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using LlamitasStore.Models;

namespace LlamitasStore.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;

    public HomeController(ILogger<HomeController> logger)
    {
        _logger = logger;
    }

    public IActionResult Index()
    {
        return View();
    }

    public IActionResult Privacy()
    {
        return View();
    }

    public IActionResult Usuario()
    {
    return View();
    }

    public IActionResult Rol()
    {
    return View();
    }

    public IActionResult Permiso()
    {
    return View();
    }
    public IActionResult Inventario()
    {
    return View();
    }

    public IActionResult Movimientos()
    {
    return View();
    }

    public IActionResult HistorialPrecios()
    {
    return View();
    }
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
    public IActionResult Marca() => View();
    public IActionResult Categoria() => View();
    public IActionResult OrdenCompra() => View();
}
