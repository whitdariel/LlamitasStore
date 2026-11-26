using System.Text;
using LlamitasStore.Infraestructura;
using LlamitasStore.Models.Entities;
using LlamitasStore.Services.Correo;
using LlamitasStore.Services.Cuentas;
using LlamitasStore.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;

namespace LlamitasStore.Controllers;

/// <summary>Registro, inicio y cierre de sesión, Google y recuperación de contraseña.</summary>
[Route("cuenta")]
public class CuentaController : Controller
{
    private readonly UserManager<Usuario> _usuarios;
    private readonly SignInManager<Usuario> _sesion;
    private readonly ServicioLoginExterno _externo;
    private readonly IEnviadorCorreo _correo;
    private readonly PlantillasCorreo _plantillas;
    private readonly ILogger<CuentaController> _logger;

    public CuentaController(UserManager<Usuario> usuarios, SignInManager<Usuario> sesion, ServicioLoginExterno externo,
        IEnviadorCorreo correo, PlantillasCorreo plantillas, ILogger<CuentaController> logger)
    {
        _usuarios = usuarios;
        _sesion = sesion;
        _externo = externo;
        _correo = correo;
        _plantillas = plantillas;
        _logger = logger;
    }

    // ---------- Inicio de sesión ----------

    [HttpGet("login")]
    public async Task<IActionResult> Login(string? returnUrl = null, string? error = null)
    {
        if (User.Identity?.IsAuthenticated == true) return Volver(returnUrl);
        if (error == "externo") TempData["Error"] = "No se pudo completar el inicio de sesión con la cuenta externa. Intenta de nuevo.";
        await CargarProveedoresAsync();
        return View(new LoginVm { ReturnUrl = returnUrl });
    }

    [HttpPost("login")]
    [EnableRateLimiting(PoliticasLimite.Formularios)]
    public async Task<IActionResult> Login(LoginVm vm)
    {
        if (!ModelState.IsValid)
        {
            await CargarProveedoresAsync();
            return View(vm);
        }

        var resultado = await _sesion.PasswordSignInAsync(vm.Email.Trim(), vm.Password, vm.Recordarme, lockoutOnFailure: true);
        if (resultado.Succeeded)
        {
            await RegistrarAccesoAsync(vm.Email);
            return Volver(vm.ReturnUrl);
        }

        ModelState.AddModelError(string.Empty, resultado switch
        {
            { IsLockedOut: true } => "Demasiados intentos fallidos. Tu cuenta quedó bloqueada 15 minutos por seguridad.",
            { IsNotAllowed: true } => "Tu cuenta está desactivada. Contacta a la tienda.",
            _ => "Correo o contraseña incorrectos."
        });
        await CargarProveedoresAsync();
        return View(vm);
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        await _sesion.SignOutAsync();
        TempData["Exito"] = "Cerraste sesión. ¡Vuelve pronto!";
        return RedirectToAction("Index", "Home");
    }

    // ---------- Registro ----------

    [HttpGet("registro")]
    public IActionResult Registro(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true) return Volver(returnUrl);
        return View(new RegistroVm { ReturnUrl = returnUrl });
    }

    [HttpPost("registro")]
    [EnableRateLimiting(PoliticasLimite.Formularios)]
    public async Task<IActionResult> Registro(RegistroVm vm)
    {
        if (!ModelState.IsValid) return View(vm);

        var correo = vm.Email.Trim();
        var usuario = new Usuario
        {
            UserName = correo,
            Email = correo,
            Nombre = vm.Nombre.Trim(),
            Apellido = vm.Apellido.Trim(),
            Estado = EstadoUsuario.Activo
        };

        var creado = await _usuarios.CreateAsync(usuario, vm.Password);
        if (!creado.Succeeded)
        {
            foreach (var e in creado.Errors.Where(e => e.Code != "DuplicateUserName")) // el de email ya lo cubre
                ModelState.AddModelError(e.Code.Contains("Password") ? nameof(vm.Password) : string.Empty, e.Description);
            return View(vm);
        }

        await _usuarios.AddToRoleAsync(usuario, Roles.Cliente);
        await _sesion.SignInAsync(usuario, isPersistent: false);
        await EnviarBienvenidaAsync(usuario);
        _logger.LogInformation("Nuevo cliente registrado: {Id}", usuario.Id);

        TempData["Exito"] = $"¡Bienvenido, {usuario.Nombre}! Tu cuenta fue creada.";
        return Volver(vm.ReturnUrl);
    }

    // ---------- Proveedores externos (Google) ----------

    [HttpPost("externo")]
    [EnableRateLimiting(PoliticasLimite.Formularios)]
    public async Task<IActionResult> Externo(string proveedor, string? returnUrl = null)
    {
        var esquemas = await _sesion.GetExternalAuthenticationSchemesAsync();
        if (!esquemas.Any(e => e.Name == proveedor)) return BadRequest();

        var callback = Url.Action(nameof(ExternoCallback), new { returnUrl });
        var propiedades = _sesion.ConfigureExternalAuthenticationProperties(proveedor, callback);
        return Challenge(propiedades, proveedor);
    }

    [HttpGet("externo-callback")]
    public async Task<IActionResult> ExternoCallback(string? returnUrl = null)
    {
        var info = await _sesion.GetExternalLoginInfoAsync();
        if (info is null)
        {
            TempData["Error"] = "No se pudo validar la sesión externa. Intenta de nuevo.";
            return RedirectToAction(nameof(Login), new { returnUrl });
        }

        var resultado = await _sesion.ExternalLoginSignInAsync(info.LoginProvider, info.ProviderKey, isPersistent: true, bypassTwoFactor: true);
        if (resultado.Succeeded)
        {
            await RegistrarAccesoPorLoginAsync(info.LoginProvider, info.ProviderKey);
            return Volver(returnUrl);
        }
        if (resultado.IsLockedOut || resultado.IsNotAllowed)
        {
            TempData["Error"] = resultado.IsLockedOut
                ? "Tu cuenta está bloqueada temporalmente por seguridad."
                : "Tu cuenta está desactivada. Contacta a la tienda.";
            return RedirectToAction(nameof(Login), new { returnUrl });
        }

        var vinculo = await _externo.VincularORegistrarAsync(info.Principal, info.LoginProvider, info.ProviderKey, info.ProviderDisplayName ?? info.LoginProvider);
        if (vinculo.Usuario is null)
        {
            TempData["Error"] = vinculo.Error;
            return RedirectToAction(nameof(Login), new { returnUrl });
        }

        await _sesion.SignInAsync(vinculo.Usuario, isPersistent: true, info.LoginProvider);
        await RegistrarAccesoAsync(vinculo.Usuario.Email!);
        if (vinculo.EsNuevo)
        {
            await EnviarBienvenidaAsync(vinculo.Usuario);
            TempData["Exito"] = $"¡Bienvenido, {vinculo.Usuario.Nombre}! Tu cuenta fue creada con {info.ProviderDisplayName}.";
        }
        return Volver(returnUrl);
    }

    // ---------- Recuperación de contraseña ----------

    [HttpGet("olvide-contrasena")]
    public IActionResult OlvideContrasena() => View(new OlvideContrasenaVm());

    [HttpPost("olvide-contrasena")]
    [EnableRateLimiting(PoliticasLimite.Formularios)]
    public async Task<IActionResult> OlvideContrasena(OlvideContrasenaVm vm, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(vm);

        var usuario = await _usuarios.FindByEmailAsync(vm.Email.Trim());
        // Misma respuesta exista o no la cuenta: no se revela qué correos están registrados.
        if (usuario is { Estado: EstadoUsuario.Activo })
        {
            var token = await _usuarios.GeneratePasswordResetTokenAsync(usuario);
            var codigo = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
            var enlace = Url.Action(nameof(RestablecerContrasena), "Cuenta", new { email = usuario.Email, codigo }, Request.Scheme)!;
            var (asunto, html) = _plantillas.RecuperarContrasena(usuario.Nombre, enlace);
            try
            {
                await _correo.EnviarAsync(usuario.Email!, asunto, html, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "No se pudo enviar el correo de recuperación al usuario {Id}", usuario.Id);
            }
        }
        return RedirectToAction(nameof(OlvideConfirmacion));
    }

    [HttpGet("olvide-contrasena/enviado")]
    public IActionResult OlvideConfirmacion() => View();

    [HttpGet("restablecer-contrasena")]
    public IActionResult RestablecerContrasena(string? email, string? codigo)
    {
        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(codigo)) return RedirectToAction(nameof(OlvideContrasena));
        return View(new RestablecerContrasenaVm { Email = email, Token = codigo });
    }

    [HttpPost("restablecer-contrasena")]
    [EnableRateLimiting(PoliticasLimite.Formularios)]
    public async Task<IActionResult> RestablecerContrasena(RestablecerContrasenaVm vm)
    {
        if (!ModelState.IsValid) return View(vm);

        var usuario = await _usuarios.FindByEmailAsync(vm.Email.Trim());
        if (usuario is null) return RedirectToAction(nameof(RestablecerConfirmacion)); // no revelar si existe

        string token;
        try { token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(vm.Token)); }
        catch (FormatException)
        {
            ModelState.AddModelError(string.Empty, "El enlace no es válido o ya expiró. Solicita uno nuevo.");
            return View(vm);
        }

        var resultado = await _usuarios.ResetPasswordAsync(usuario, token, vm.Password);
        if (!resultado.Succeeded)
        {
            foreach (var e in resultado.Errors)
                ModelState.AddModelError(e.Code == "InvalidToken" ? string.Empty : nameof(vm.Password),
                    e.Code == "InvalidToken" ? "El enlace no es válido o ya expiró. Solicita uno nuevo." : e.Description);
            return View(vm);
        }

        // Desbloquear si estaba bloqueada por intentos fallidos: demostró ser dueña del correo.
        await _usuarios.SetLockoutEndDateAsync(usuario, null);
        await _usuarios.ResetAccessFailedCountAsync(usuario);
        return RedirectToAction(nameof(RestablecerConfirmacion));
    }

    [HttpGet("restablecer-contrasena/listo")]
    public IActionResult RestablecerConfirmacion() => View();

    [HttpGet("acceso-denegado")]
    public IActionResult AccesoDenegado()
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return View();
    }

    // ---------- Auxiliares ----------

    private IActionResult Volver(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl) : RedirectToAction("Index", "Home");

    private async Task CargarProveedoresAsync() =>
        ViewData["Proveedores"] = (await _sesion.GetExternalAuthenticationSchemesAsync()).ToList();

    private async Task RegistrarAccesoAsync(string email)
    {
        var usuario = await _usuarios.FindByEmailAsync(email);
        if (usuario is null) return;
        usuario.UltimoAcceso = DateTime.UtcNow;
        await _usuarios.UpdateAsync(usuario);
    }

    private async Task RegistrarAccesoPorLoginAsync(string proveedor, string clave)
    {
        var usuario = await _usuarios.FindByLoginAsync(proveedor, clave);
        if (usuario?.Email is not null) await RegistrarAccesoAsync(usuario.Email);
    }

    private async Task EnviarBienvenidaAsync(Usuario usuario)
    {
        try
        {
            var (asunto, html) = _plantillas.Bienvenida(usuario.Nombre, Url.Action("Index", "Home", null, Request.Scheme)!);
            await _correo.EnviarAsync(usuario.Email!, asunto, html);
        }
        catch (Exception ex)
        {
            // La bienvenida no es crítica: el registro ya se completó.
            _logger.LogWarning(ex, "No se pudo enviar la bienvenida al usuario {Id}", usuario.Id);
        }
    }
}
