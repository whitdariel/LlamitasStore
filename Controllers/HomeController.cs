using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using LlamitasStore.Models;
using LlamitasStore.Data;

namespace LlamitasStore.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;
    private readonly AppDbContext _context;

    public HomeController(ILogger<HomeController> logger, AppDbContext context)
    {
        _logger = logger;
        _context = context;
    }

    public IActionResult Index() => View();
    public IActionResult Privacy() => View();

    // ================= USUARIOS =================
    public IActionResult Usuario()
    {
        ViewBag.Roles = _context.Roles.ToList();
        var lista = _context.Usuarios.Include(u => u.Rol).ToList();
        return View(lista);
    }

    [HttpPost]
    public IActionResult CrearUsuario(Usuario usuario)
    {
        _context.Usuarios.Add(usuario);
        _context.SaveChanges();
        return RedirectToAction("Usuario");
    }

    // ================= ROLES =================
    public IActionResult Rol()
    {
        var lista = _context.Roles.ToList();
        return View(lista);
    }

    [HttpPost]
    public IActionResult CrearRol(Rol rol)
    {
        _context.Roles.Add(rol);
        _context.SaveChanges();
        return RedirectToAction("Rol");
    }

    // ================= PERMISOS =================
    public IActionResult Permiso()
    {
        var lista = _context.Permisos.ToList();
        return View(lista);
    }

    // ================= CATEGORÍAS =================
    public IActionResult Categoria()
    {
        var lista = _context.Categorias.ToList();
        return View(lista);
    }

    [HttpPost]
    public IActionResult CrearCategoria(Categoria categoria)
    {
        _context.Categorias.Add(categoria);
        _context.SaveChanges();
        return RedirectToAction("Categoria");
    }

    // ================= VISTAS SIN TABLA PROPIA EN SQL =================
    public IActionResult Marca() => View(new List<Marca>());
    public IActionResult OrdenCompra() => View(new List<OrdenCompra>());
    public IActionResult Inventario() => View();
    public IActionResult Movimientos() => View();
    public IActionResult HistorialPrecios() => View();

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}