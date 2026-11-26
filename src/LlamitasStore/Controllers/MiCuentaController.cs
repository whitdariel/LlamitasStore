using LlamitasStore.Models.Entities;
using LlamitasStore.Services.Catalogo;
using LlamitasStore.Services.Cuentas;
using LlamitasStore.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace LlamitasStore.Controllers;

/// <summary>Área privada del cliente. Todo se resuelve con el usuario de la sesión, nunca con un id enviado por el navegador.</summary>
[Authorize]
[Route("cuenta")]
public class MiCuentaController : Controller
{
    private readonly UserManager<Usuario> _usuarios;
    private readonly SignInManager<Usuario> _sesion;
    private readonly ServicioDirecciones _direcciones;
    private readonly ServicioFavoritos _favoritos;
    private readonly ServicioCatalogo _catalogo;
    private readonly Services.Ventas.ServicioPedidos _pedidos;
    private readonly Services.AjustesTienda _tienda;

    public MiCuentaController(UserManager<Usuario> usuarios, SignInManager<Usuario> sesion, ServicioDirecciones direcciones,
        ServicioFavoritos favoritos, ServicioCatalogo catalogo, Services.Ventas.ServicioPedidos pedidos, Services.AjustesTienda tienda)
    {
        _usuarios = usuarios;
        _sesion = sesion;
        _direcciones = direcciones;
        _favoritos = favoritos;
        _catalogo = catalogo;
        _pedidos = pedidos;
        _tienda = tienda;
    }

    private int UsuarioId => int.Parse(_usuarios.GetUserId(User)!);

    // ---------- Resumen ----------

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var usuario = await _usuarios.GetUserAsync(User);
        if (usuario is null) return Challenge();
        var direcciones = await _direcciones.ListarAsync(usuario.Id, ct);
        return View(new ResumenCuentaVm(usuario, (await _usuarios.GetRolesAsync(usuario)).ToList(),
            await _favoritos.ContarAsync(usuario.Id, ct), direcciones.Count, direcciones.FirstOrDefault(d => d.EsPredeterminada)));
    }

    // ---------- Perfil ----------

    [HttpGet("perfil")]
    public async Task<IActionResult> Perfil()
    {
        var u = await _usuarios.GetUserAsync(User);
        if (u is null) return Challenge();
        return View(new PerfilVm { Nombre = u.Nombre, Apellido = u.Apellido, Telefono = u.PhoneNumber, Email = u.Email! });
    }

    [HttpPost("perfil")]
    public async Task<IActionResult> Perfil(PerfilVm vm)
    {
        var u = await _usuarios.GetUserAsync(User);
        if (u is null) return Challenge();
        vm.Email = u.Email!;
        if (!ModelState.IsValid) return View(vm);

        u.Nombre = vm.Nombre.Trim();
        u.Apellido = vm.Apellido.Trim();
        u.PhoneNumber = string.IsNullOrWhiteSpace(vm.Telefono) ? null : vm.Telefono.Trim();
        var r = await _usuarios.UpdateAsync(u);
        if (!r.Succeeded)
        {
            foreach (var e in r.Errors) ModelState.AddModelError(string.Empty, e.Description);
            return View(vm);
        }
        await _sesion.RefreshSignInAsync(u); // actualiza el nombre que muestra el header
        TempData["Exito"] = "Tus datos se guardaron.";
        return RedirectToAction(nameof(Perfil));
    }

    // ---------- Seguridad ----------

    [HttpGet("seguridad")]
    public async Task<IActionResult> Seguridad()
    {
        var u = await _usuarios.GetUserAsync(User);
        if (u is null) return Challenge();
        return View(await ArmarSeguridadAsync(u, new CambiarContrasenaVm()));
    }

    [HttpPost("seguridad")]
    public async Task<IActionResult> Seguridad(CambiarContrasenaVm vm)
    {
        var u = await _usuarios.GetUserAsync(User);
        if (u is null) return Challenge();
        var tiene = await _usuarios.HasPasswordAsync(u);
        if (tiene && string.IsNullOrEmpty(vm.Actual))
            ModelState.AddModelError(nameof(vm.Actual), "Escribe tu contraseña actual.");
        if (!ModelState.IsValid) return View(await ArmarSeguridadAsync(u, vm));

        var r = tiene
            ? await _usuarios.ChangePasswordAsync(u, vm.Actual!, vm.Nueva)
            : await _usuarios.AddPasswordAsync(u, vm.Nueva);
        if (!r.Succeeded)
        {
            foreach (var e in r.Errors)
                ModelState.AddModelError(e.Code == "PasswordMismatch" ? nameof(vm.Actual) : nameof(vm.Nueva), e.Description);
            return View(await ArmarSeguridadAsync(u, vm));
        }

        await _sesion.RefreshSignInAsync(u); // el SecurityStamp cambió: se renueva la sesión actual
        TempData["Exito"] = tiene ? "Tu contraseña se actualizó." : "Listo: ahora también puedes entrar con tu correo y contraseña.";
        return RedirectToAction(nameof(Seguridad));
    }

    private async Task<CambiarContrasenaVm> ArmarSeguridadAsync(Usuario u, CambiarContrasenaVm vm)
    {
        vm.TienePassword = await _usuarios.HasPasswordAsync(u);
        vm.CuentasExternas = (await _usuarios.GetLoginsAsync(u)).Select(l => l.ProviderDisplayName ?? l.LoginProvider).ToList();
        vm.Actual = null; vm.Nueva = string.Empty; vm.Confirmar = string.Empty; // nunca se devuelven contraseñas a la vista
        return vm;
    }

    // ---------- Direcciones ----------

    [HttpGet("direcciones")]
    public async Task<IActionResult> Direcciones(CancellationToken ct) => View(await _direcciones.ListarAsync(UsuarioId, ct));

    [HttpGet("direcciones/nueva")]
    public IActionResult NuevaDireccion(string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = Url.IsLocalUrl(returnUrl) ? returnUrl : null;
        return View("Direccion", new DireccionVm());
    }

    [HttpPost("direcciones/nueva")]
    public async Task<IActionResult> NuevaDireccion(DireccionVm vm, string? returnUrl, CancellationToken ct)
    {
        vm.Id = null;
        ViewData["ReturnUrl"] = Url.IsLocalUrl(returnUrl) ? returnUrl : null;
        if (!ModelState.IsValid) return View("Direccion", vm);
        var r = await _direcciones.CrearAsync(UsuarioId, vm, ct);
        // Desde el checkout: volver allí con la dirección ya disponible.
        if (r == ResultadoDireccion.Ok && Url.IsLocalUrl(returnUrl))
        {
            TempData["Exito"] = "Dirección guardada.";
            return LocalRedirect(returnUrl);
        }
        return Resultado(r, vm, "Dirección guardada.");
    }

    [HttpGet("direcciones/{id:int}")]
    public async Task<IActionResult> EditarDireccion(int id, CancellationToken ct)
    {
        var d = await _direcciones.ObtenerAsync(UsuarioId, id, ct);
        return d is null ? NotFound() : View("Direccion", DireccionVm.Desde(d));
    }

    [HttpPost("direcciones/{id:int}")]
    public async Task<IActionResult> EditarDireccion(int id, DireccionVm vm, CancellationToken ct)
    {
        vm.Id = id;
        if (!ModelState.IsValid) return View("Direccion", vm);
        return Resultado(await _direcciones.EditarAsync(UsuarioId, id, vm, ct), vm, "Dirección actualizada.");
    }

    [HttpPost("direcciones/{id:int}/predeterminada")]
    public async Task<IActionResult> MarcarPredeterminada(int id, CancellationToken ct) =>
        Resultado(await _direcciones.MarcarPredeterminadaAsync(UsuarioId, id, ct), null, "Dirección predeterminada actualizada.");

    [HttpPost("direcciones/{id:int}/eliminar")]
    public async Task<IActionResult> EliminarDireccion(int id, CancellationToken ct) =>
        Resultado(await _direcciones.EliminarAsync(UsuarioId, id, ct), null, "Dirección eliminada.");

    private IActionResult Resultado(ResultadoDireccion r, DireccionVm? vm, string exito)
    {
        switch (r)
        {
            case ResultadoDireccion.Ok:
                TempData["Exito"] = exito;
                return RedirectToAction(nameof(Direcciones));
            case ResultadoDireccion.NoEncontrada:
                return NotFound();
            case ResultadoDireccion.LimiteAlcanzado:
                TempData["Error"] = $"Puedes guardar hasta {ServicioDirecciones.Maximo} direcciones. Elimina alguna para agregar otra.";
                return RedirectToAction(nameof(Direcciones));
            default:
                ModelState.AddModelError(nameof(DireccionVm.Departamento), "Elige un departamento de la lista.");
                return View("Direccion", vm);
        }
    }

    // ---------- Pedidos ----------

    [HttpGet("pedidos")]
    public async Task<IActionResult> Pedidos(CancellationToken ct) => View(await _pedidos.ListarAsync(UsuarioId, ct));

    [HttpGet("pedidos/{numero}")]
    public async Task<IActionResult> Pedido(string numero, CancellationToken ct)
    {
        var pedido = await _pedidos.ObtenerAsync(UsuarioId, numero, ct);
        if (pedido is null) return NotFound();
        ViewData["Instrucciones"] = _tienda.Obtener(Models.Entities.ClavesAjuste.DatosTransferencia);
        return View(pedido);
    }

    [HttpPost("pedidos/{numero}/cancelar")]
    public async Task<IActionResult> CancelarPedido(string numero, CancellationToken ct)
    {
        var ok = await _pedidos.CancelarPorClienteAsync(UsuarioId, numero, ct);
        TempData[ok ? "Exito" : "Error"] = ok
            ? $"Cancelaste el pedido {numero}."
            : "Este pedido ya no se puede cancelar desde la tienda. Escríbenos si necesitas ayuda.";
        return RedirectToAction(nameof(Pedido), new { numero });
    }

    // ---------- Configuraciones del PC Builder ----------

    [HttpGet("configuraciones")]
    public async Task<IActionResult> Configuraciones([FromServices] Services.PcBuilder.ServicioPcBuilder builder, CancellationToken ct) =>
        View(await builder.ListarAsync(UsuarioId, ct));

    [HttpPost("configuraciones/{id:int}/eliminar")]
    public async Task<IActionResult> EliminarConfiguracion(int id, [FromServices] Services.PcBuilder.ServicioPcBuilder builder, CancellationToken ct)
    {
        if (!await builder.EliminarAsync(UsuarioId, id, ct)) return NotFound();
        TempData["Exito"] = "Configuración eliminada.";
        return RedirectToAction(nameof(Configuraciones));
    }

    // ---------- Favoritos ----------

    [HttpGet("favoritos")]
    public async Task<IActionResult> Favoritos(CancellationToken ct)
    {
        var ids = await _favoritos.ListarIdsAsync(UsuarioId, ct);
        return View(await _catalogo.ObtenerTarjetasAsync(ids, ct: ct));
    }

    /// <summary>Agrega o quita un favorito. Responde JSON si lo pide el script; si no, vuelve a la página anterior.</summary>
    [HttpPost("favoritos/{productoId:int}/alternar")]
    public async Task<IActionResult> AlternarFavorito(int productoId, string? returnUrl, CancellationToken ct)
    {
        var r = await _favoritos.AlternarAsync(UsuarioId, productoId, ct);
        if (Request.Headers.XRequestedWith == "fetch")
            return r.Encontrado ? Json(new { favorito = r.EsFavorito, total = r.Total }) : NotFound();

        if (!r.Encontrado) return NotFound();
        TempData["Exito"] = r.EsFavorito ? "Agregado a tus favoritos." : "Quitado de tus favoritos.";
        return !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl) : RedirectToAction(nameof(Favoritos));
    }
}
