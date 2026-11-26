using LlamitasStore.Services.PcBuilder;
using LlamitasStore.Services.Ventas;
using LlamitasStore.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace LlamitasStore.Controllers;

public record PcBuilderVm(ArmadoPc Armado, PasoPc Paso, IReadOnlyList<OpcionPc> Opciones, bool MostrarTodas, string? NombreConfiguracion);

/// <summary>Configurador de PC. Todo funciona con enlaces GET (sin JS) y la selección viaja en la URL.</summary>
[Route("pc-builder")]
public class PcBuilderController : Controller
{
    private readonly ServicioPcBuilder _builder;
    private readonly CarritoActual _carritoActual;
    private readonly UserManager<Usuario> _usuarios;

    public PcBuilderController(ServicioPcBuilder builder, CarritoActual carritoActual, UserManager<Usuario> usuarios)
    {
        _builder = builder;
        _carritoActual = carritoActual;
        _usuarios = usuarios;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(string? paso, bool todos = false, CancellationToken ct = default)
    {
        var seleccion = SeleccionPc.Desde(Request.Query);
        var armado = await _builder.ArmarAsync(seleccion, ct);
        var pasoActual = SeleccionPc.Paso(paso) ?? SiguientePaso(armado);
        var opciones = await _builder.OpcionesAsync(armado, pasoActual, ct);
        if (!todos) opciones = opciones.Where(o => o.Elegida || o.Estado != Severidad.Error).ToList();

        string? nombre = null;
        if (seleccion.ConfiguracionId is { } id && User.Identity?.IsAuthenticated == true)
            nombre = (await _builder.AbrirAsync(int.Parse(_usuarios.GetUserId(User)!), id, ct))?.Nombre;

        ViewData["Title"] = "PC Builder";
        return View(new PcBuilderVm(armado, pasoActual, opciones, todos, nombre));
    }

    /// <summary>Cambia la cantidad de kits de RAM (formulario GET, sin JS).</summary>
    [HttpGet("cantidad")]
    public IActionResult Cantidad(string tipo, int id, int cantidad)
    {
        var seleccion = SeleccionPc.Desde(Request.Query);
        var paso = SeleccionPc.Paso(tipo);
        if (paso is null || paso.Multiple) return Redirect(seleccion.Query());
        seleccion.Poner(paso.Tipo, id, Math.Clamp(cantidad, 1, paso.MaxCantidad));
        return Redirect(seleccion.Query(paso.Clave));
    }

    [HttpPost("al-carrito")]
    public async Task<IActionResult> AlCarrito(CancellationToken ct)
    {
        var seleccion = SeleccionPc.Desde(new QueryCollection(Request.Form.ToDictionary(kv => kv.Key, kv => kv.Value)));
        var (agregados, problemas) = await _builder.AgregarAlCarritoAsync(_carritoActual.Dueno(crearToken: true), seleccion, ct);
        if (problemas.Count > 0) TempData["Error"] = string.Join(" ", problemas);
        if (agregados > 0) TempData["Exito"] = $"Agregaste {agregados} componente(s) de tu armado al carrito.";
        return RedirectToAction("Index", "Carrito");
    }

    [Authorize]
    [HttpPost("guardar")]
    public async Task<IActionResult> Guardar(string? nombre, CancellationToken ct)
    {
        var seleccion = SeleccionPc.Desde(new QueryCollection(Request.Form.ToDictionary(kv => kv.Key, kv => kv.Value)));
        var (id, error) = await _builder.GuardarAsync(int.Parse(_usuarios.GetUserId(User)!), seleccion, nombre, ct);
        if (error is not null)
        {
            TempData["Error"] = error;
            return Redirect(seleccion.Query());
        }
        seleccion.ConfiguracionId = id;
        TempData["Exito"] = "Configuración guardada. La encuentras en Mi cuenta → Mis PCs.";
        return Redirect(seleccion.Query());
    }

    private static PasoPc SiguientePaso(ArmadoPc armado) =>
        SeleccionPc.Pasos.FirstOrDefault(p => !armado.De(p.Tipo).Any()) ?? SeleccionPc.Pasos[0];
}
