using LlamitasStore.Infraestructura;
using LlamitasStore.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace LlamitasStore.Areas.Admin.Controllers;

/// <summary>
/// Base del panel. Exige sesión; cada acción declara además su permiso con [Permiso(módulo, acción)],
/// que se verifica en el servidor contra la matriz rol × módulo.
/// </summary>
[Area("Admin")]
[Authorize]
public abstract class AdminController : Controller
{
    protected int UsuarioId => int.Parse(HttpContext.RequestServices.GetRequiredService<UserManager<Usuario>>().GetUserId(User)!);

    protected IActionResult Resultado(string? error, string exito, string accionRetorno, object? valores = null)
    {
        TempData[error is null ? "Exito" : "Error"] = error ?? exito;
        return RedirectToAction(accionRetorno, valores);
    }

    protected const int TamanoPagina = 20;

    protected static (int Pagina, int Saltar) Paginar(int pagina) => (Math.Max(1, pagina), (Math.Max(1, pagina) - 1) * TamanoPagina);
}

/// <summary>Atajos de permisos para no repetir el nombre del módulo.</summary>
public static class P
{
    public const AccionPermiso Leer = AccionPermiso.Leer, Crear = AccionPermiso.Crear, Editar = AccionPermiso.Editar, Eliminar = AccionPermiso.Eliminar;
}
