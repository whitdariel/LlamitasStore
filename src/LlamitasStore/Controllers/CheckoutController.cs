using LlamitasStore.Infraestructura;
using LlamitasStore.Models.Entities;
using LlamitasStore.Services;
using LlamitasStore.Services.Correo;
using LlamitasStore.Services.Cuentas;
using LlamitasStore.Services.Ventas;
using LlamitasStore.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LlamitasStore.Controllers;

/// <summary>Finalizar compra: dirección, método de pago y confirmación. Requiere sesión.</summary>
[Authorize]
[Route("checkout")]
public class CheckoutController : Controller
{
    private readonly ServicioCarrito _carrito;
    private readonly ServicioPedidos _pedidos;
    private readonly ServicioDirecciones _direcciones;
    private readonly UserManager<Usuario> _usuarios;
    private readonly AjustesTienda _tienda;
    private readonly IEnviadorCorreo _correo;
    private readonly PlantillasCorreo _plantillas;
    private readonly ILogger<CheckoutController> _logger;

    public CheckoutController(ServicioCarrito carrito, ServicioPedidos pedidos, ServicioDirecciones direcciones, UserManager<Usuario> usuarios,
        AjustesTienda tienda, IEnviadorCorreo correo, PlantillasCorreo plantillas, ILogger<CheckoutController> logger)
    {
        _carrito = carrito;
        _pedidos = pedidos;
        _direcciones = direcciones;
        _usuarios = usuarios;
        _tienda = tienda;
        _correo = correo;
        _plantillas = plantillas;
        _logger = logger;
    }

    private int UsuarioId => int.Parse(_usuarios.GetUserId(User)!);
    private DuenoCarrito Dueno => new(UsuarioId, null);

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var carrito = await _carrito.ObtenerAsync(Dueno, ct);
        if (carrito.EstaVacio) return RedirectToAction("Index", "Carrito");

        var direcciones = await _direcciones.ListarAsync(UsuarioId, ct);
        var vm = new CheckoutVm { DireccionId = direcciones.FirstOrDefault(d => d.EsPredeterminada)?.Id, MetodoPago = MetodoPago.ContraEntrega };
        return View(Pagina(carrito, direcciones, vm));
    }

    [HttpPost("confirmar")]
    [EnableRateLimiting(PoliticasLimite.Formularios)]
    public async Task<IActionResult> Confirmar(CheckoutVm vm, CancellationToken ct)
    {
        var carrito = await _carrito.ObtenerAsync(Dueno, ct);
        // Doble clic o pestaña vieja: si el carrito ya se convirtió en pedido, no se crea otro.
        if (carrito.EstaVacio) return RedirectToAction("Pedidos", "MiCuenta");

        if (!carrito.PuedeComprar)
        {
            TempData["Error"] = "Algunos productos de tu carrito cambiaron de disponibilidad. Revísalo antes de continuar.";
            return RedirectToAction("Index", "Carrito");
        }

        if (!ModelState.IsValid)
            return View("Index", Pagina(carrito, await _direcciones.ListarAsync(UsuarioId, ct), vm));

        var r = await _pedidos.CrearDesdeCarritoAsync(UsuarioId, vm, ct);
        if (!r.Exito)
        {
            foreach (var e in r.Errores) ModelState.AddModelError(string.Empty, e);
            var actualizado = await _carrito.ObtenerAsync(Dueno, ct);
            return View("Index", Pagina(actualizado, await _direcciones.ListarAsync(UsuarioId, ct), vm));
        }

        await EnviarConfirmacionAsync(r.Numero!, ct);
        return RedirectToAction(nameof(Confirmado), new { numero = r.Numero });
    }

    [HttpGet("confirmado/{numero}")]
    public async Task<IActionResult> Confirmado(string numero, CancellationToken ct)
    {
        var pedido = await _pedidos.ObtenerAsync(UsuarioId, numero, ct);
        if (pedido is null) return NotFound();
        ViewData["Instrucciones"] = _tienda.Obtener(ClavesAjuste.DatosTransferencia);
        return View(pedido);
    }

    private CheckoutPaginaVm Pagina(CarritoVista carrito, IReadOnlyList<Direccion> direcciones, CheckoutVm vm) =>
        new(carrito, direcciones, vm, _tienda.Obtener(ClavesAjuste.DatosTransferencia));

    private async Task EnviarConfirmacionAsync(string numero, CancellationToken ct)
    {
        try
        {
            var usuario = await _usuarios.GetUserAsync(User);
            var pedido = await _pedidos.ObtenerAsync(UsuarioId, numero, ct);
            if (usuario?.Email is null || pedido is null) return;
            var (asunto, html) = _plantillas.PedidoRecibido(usuario.Nombre, numero, _tienda.Dinero(pedido.Total),
                TextosPedido.Metodo(pedido.MetodoPago), Url.Action("Pedido", "MiCuenta", new { numero }, Request.Scheme)!,
                pedido.MetodoPago == MetodoPago.Transferencia ? _tienda.Obtener(ClavesAjuste.DatosTransferencia) : null);
            await _correo.EnviarAsync(usuario.Email, asunto, html, ct);
        }
        catch (Exception ex)
        {
            // El pedido ya está registrado: un fallo del correo no debe deshacerlo.
            _logger.LogWarning(ex, "No se pudo enviar la confirmación del pedido {Numero}", numero);
        }
    }
}
