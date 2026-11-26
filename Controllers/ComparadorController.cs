using LlamitasStore.Data;
using LlamitasStore.Models.Entities;
using LlamitasStore.Services.Catalogo;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LlamitasStore.Controllers;

[Route("comparar")]
public class ComparadorController : Controller
{
    private readonly ServicioComparador _comparador;
    private readonly ComparadorActual _actual;
    private readonly AppDbContext _db;

    public ComparadorController(ServicioComparador comparador, ComparadorActual actual, AppDbContext db)
    {
        _comparador = comparador;
        _actual = actual;
        _db = db;
    }

    /// <summary>Con ?ids= muestra esa comparación (enlace compartido); si no, la lista guardada.</summary>
    [HttpGet("")]
    public async Task<IActionResult> Index(string? ids, CancellationToken ct)
    {
        var lista = ids is null ? _actual.Ids.ToList() : ServicioComparador.LeerIds(ids);
        ViewData["Compartida"] = ids is not null;
        return View(await _comparador.CompararAsync(lista, ct));
    }

    [HttpPost("agregar")]
    public async Task<IActionResult> Agregar(int productoId, string? returnUrl, CancellationToken ct)
    {
        if (!await _db.Productos.AnyAsync(p => p.Id == productoId && p.Estado != EstadoProducto.Inactivo, ct)) return NotFound();
        var ids = _actual.Ids.ToList();
        bool ok;
        if (ids.Contains(productoId)) ok = true;
        else if (ids.Count >= ServicioComparador.Maximo) ok = false;
        else
        {
            ids.Add(productoId);
            _actual.Guardar(ids);
            ok = true;
        }

        if (Request.Headers.XRequestedWith == "fetch")
            return Json(new { ok, total = ids.Count, maximo = ServicioComparador.Maximo });

        TempData[ok ? "Exito" : "Error"] = ok
            ? $"Agregado al comparador ({ids.Count} de {ServicioComparador.Maximo})."
            : $"El comparador admite hasta {ServicioComparador.Maximo} productos. Quita alguno para agregar otro.";
        return Volver(returnUrl);
    }

    [HttpPost("quitar")]
    public IActionResult Quitar(int productoId, string? returnUrl)
    {
        _actual.Guardar(_actual.Ids.Where(id => id != productoId));
        if (Request.Headers.XRequestedWith == "fetch") return Json(new { ok = true, total = _actual.Ids.Count });
        return Volver(returnUrl);
    }

    [HttpPost("vaciar")]
    public IActionResult Vaciar()
    {
        _actual.Guardar([]);
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Guarda como lista propia una comparación abierta desde un enlace compartido.</summary>
    [HttpPost("usar")]
    public IActionResult Usar(string? ids)
    {
        _actual.Guardar(ServicioComparador.LeerIds(ids));
        return RedirectToAction(nameof(Index));
    }

    private IActionResult Volver(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl) : RedirectToAction(nameof(Index));
}
