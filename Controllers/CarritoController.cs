using LlamitasStore.Services.Ventas;
using LlamitasStore.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace LlamitasStore.Controllers;

/// <summary>Carrito para visitantes y usuarios. Responde JSON cuando lo pide el script; si no, redirige (funciona sin JS).</summary>
[Route("carrito")]
public class CarritoController : Controller
{
    private readonly ServicioCarrito _carrito;
    private readonly CarritoActual _actual;

    public CarritoController(ServicioCarrito carrito, CarritoActual actual)
    {
        _carrito = carrito;
        _actual = actual;
    }

    private bool EsFetch => Request.Headers.XRequestedWith == "fetch";

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        ViewData["Title"] = "Carrito";
        return View(await _carrito.ObtenerAsync(_actual.Dueno(), ct));
    }

    [HttpPost("agregar")]
    public async Task<IActionResult> Agregar(int productoId, int cantidad = 1, string? returnUrl = null, CancellationToken ct = default)
    {
        var r = await _carrito.AgregarAsync(_actual.Dueno(crearToken: true), productoId, cantidad, ct);
        var ok = r.Estado is EstadoAgregar.Agregado or EstadoAgregar.Ajustado;

        if (EsFetch) return Json(new { ok, mensaje = r.Mensaje, total = r.TotalItems, cantidad = r.CantidadEnCarrito });

        TempData[ok ? "Exito" : "Error"] = r.Mensaje;
        return ok ? RedirectToAction(nameof(Index)) : Volver(returnUrl);
    }

    [HttpPost("actualizar")]
    public async Task<IActionResult> Actualizar(int productoId, int cantidad, CancellationToken ct)
    {
        await _carrito.ActualizarAsync(_actual.Dueno(), productoId, cantidad, ct);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("quitar")]
    public async Task<IActionResult> Quitar(int productoId, CancellationToken ct)
    {
        await _carrito.QuitarAsync(_actual.Dueno(), productoId, ct);
        TempData["Exito"] = "Producto quitado del carrito.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("vaciar")]
    public async Task<IActionResult> Vaciar(CancellationToken ct)
    {
        await _carrito.VaciarAsync(_actual.Dueno(), ct);
        TempData["Exito"] = "Vaciaste tu carrito.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("cupon")]
    public async Task<IActionResult> AplicarCupon(string? codigo, CancellationToken ct)
    {
        var v = await _carrito.AplicarCuponAsync(_actual.Dueno(), codigo, ct);
        TempData[v.Valido ? "Exito" : "Error"] = v.Valido ? $"Cupón {v.Cupon!.Codigo} aplicado." : v.Error;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("cupon/quitar")]
    public async Task<IActionResult> QuitarCupon(CancellationToken ct)
    {
        await _carrito.QuitarCuponAsync(_actual.Dueno(), ct);
        return RedirectToAction(nameof(Index));
    }

    private IActionResult Volver(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl) : RedirectToAction(nameof(Index));
}
