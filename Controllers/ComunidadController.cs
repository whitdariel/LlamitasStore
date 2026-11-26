using LlamitasStore.Data;
using LlamitasStore.Infraestructura;
using LlamitasStore.Models.Entities;
using LlamitasStore.Services.Comunidad;
using LlamitasStore.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace LlamitasStore.Controllers;

/// <summary>Reseñas de clientes. El autor siempre es el usuario de la sesión.</summary>
[Authorize]
[Route("producto/{productoId:int}/resena")]
public class ResenasController : Controller
{
    private readonly ServicioResenas _resenas;
    private readonly AppDbContext _db;
    private readonly UserManager<Usuario> _usuarios;

    public ResenasController(ServicioResenas resenas, AppDbContext db, UserManager<Usuario> usuarios)
    {
        _resenas = resenas;
        _db = db;
        _usuarios = usuarios;
    }

    private int UsuarioId => int.Parse(_usuarios.GetUserId(User)!);

    [HttpPost("")]
    [EnableRateLimiting(PoliticasLimite.Formularios)]
    public async Task<IActionResult> Guardar(int productoId, ResenaVm vm, CancellationToken ct)
    {
        var slug = await SlugAsync(productoId, ct);
        if (slug is null) return NotFound();
        if (!ModelState.IsValid)
        {
            var errores = ModelState.Where(kv => kv.Value!.Errors.Count > 0)
                .Select(kv => $"{(kv.Key == nameof(ResenaVm.Calificacion) ? "" : kv.Key + ": ")}{kv.Value!.Errors[0].ErrorMessage}");
            TempData["Error"] = "No se guardó tu reseña. " + string.Join(" ", errores);
            return VolverAFicha(slug);
        }

        var (r, error) = await _resenas.GuardarAsync(UsuarioId, productoId, new DatosResena(vm.Calificacion, vm.Titulo, vm.Comentario), ct);
        TempData[error is null ? "Exito" : "Error"] = r switch
        {
            ResultadoResena.Creada => "¡Gracias! Tu reseña ya está publicada.",
            ResultadoResena.Actualizada => "Actualizamos tu reseña.",
            _ => error
        };
        return VolverAFicha(slug);
    }

    [HttpPost("eliminar")]
    public async Task<IActionResult> Eliminar(int productoId, CancellationToken ct)
    {
        var slug = await SlugAsync(productoId, ct);
        if (slug is null) return NotFound();
        if (await _resenas.EliminarAsync(UsuarioId, productoId, ct)) TempData["Exito"] = "Eliminaste tu reseña.";
        return VolverAFicha(slug);
    }

    private Task<string?> SlugAsync(int productoId, CancellationToken ct) =>
        _db.Productos.Where(p => p.Id == productoId && p.Estado != EstadoProducto.Inactivo).Select(p => p.Slug).FirstOrDefaultAsync(ct);

    private RedirectResult VolverAFicha(string slug) => Redirect($"/producto/{slug}?seccion=resenas#detalle");
}

[Route("contacto")]
public class ContactoController : Controller
{
    private readonly ServicioContacto _contacto;
    private readonly UserManager<Usuario> _usuarios;

    public ContactoController(ServicioContacto contacto, UserManager<Usuario> usuarios)
    {
        _contacto = contacto;
        _usuarios = usuarios;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(string? asunto, string? pedido)
    {
        var vm = new ContactoVm
        {
            Asunto = ContactoVm.Asuntos.Contains(asunto) ? asunto! : "",
            NumeroPedido = pedido?.Length <= 30 ? pedido : null
        };
        if (await _usuarios.GetUserAsync(User) is { } u)
        {
            vm.Nombre = $"{u.Nombre} {u.Apellido}".Trim();
            vm.Email = u.Email ?? "";
        }
        return View(vm);
    }

    [HttpPost("")]
    [EnableRateLimiting(PoliticasLimite.Formularios)]
    public async Task<IActionResult> Index(ContactoVm vm, CancellationToken ct)
    {
        if (!ContactoVm.Asuntos.Contains(vm.Asunto)) ModelState.AddModelError(nameof(vm.Asunto), "Elige un asunto de la lista.");
        if (!ModelState.IsValid) return View(vm);

        // Un bot que rellena el campo oculto recibe la misma respuesta, pero no se guarda nada.
        if (string.IsNullOrEmpty(vm.SitioWeb)) await _contacto.GuardarAsync(vm, ct);

        TempData["Exito"] = "Recibimos tu mensaje. Te responderemos al correo que indicaste.";
        return RedirectToAction(nameof(Index));
    }
}

/// <summary>Páginas informativas. Los montos y datos de contacto salen de la configuración de la tienda.</summary>
public class InfoController : Controller
{
    [HttpGet("ayuda")]
    public IActionResult Ayuda() => View();

    [HttpGet("politicas/envios")]
    public IActionResult Envios() => View();

    [HttpGet("politicas/devoluciones")]
    public IActionResult Devoluciones() => View();

    [HttpGet("politicas/terminos")]
    public IActionResult Terminos() => View();

    [HttpGet("politicas/privacidad")]
    public IActionResult Privacidad() => View();
}
