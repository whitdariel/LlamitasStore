using LlamitasStore.Infraestructura;
using LlamitasStore.Models.Entities;
using LlamitasStore.Services.Admin;
using Microsoft.AspNetCore.Mvc;

namespace LlamitasStore.Areas.Admin.Controllers;

[Route("admin")]
public class DashboardController : AdminController
{
    private readonly ServicioDashboard _dashboard;
    private readonly ServicioPermisos _permisos;

    public DashboardController(ServicioDashboard dashboard, ServicioPermisos permisos)
    {
        _dashboard = dashboard;
        _permisos = permisos;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        if (!await _permisos.PuedeEntrarAlPanelAsync(User)) return Forbid();

        // Personal sin acceso al dashboard: se lo lleva al primer módulo que sí puede ver.
        if (!await _permisos.TieneAsync(User, Modulos.Dashboard, AccionPermiso.Leer))
        {
            foreach (var (modulo, url) in MenuAdmin.Rutas)
                if (await _permisos.TieneAsync(User, modulo, AccionPermiso.Leer)) return Redirect(url);
            return Forbid();
        }

        return View(await _dashboard.ObtenerAsync(ct));
    }
}

public static class MenuAdmin
{
    public static readonly (string Modulo, string Url)[] Rutas =
    [
        (Modulos.Pedidos, "/admin/pedidos"), (Modulos.Productos, "/admin/productos"), (Modulos.Inventario, "/admin/inventario"),
        (Modulos.Categorias, "/admin/categorias"), (Modulos.Marcas, "/admin/marcas"), (Modulos.Proveedores, "/admin/proveedores"),
        (Modulos.Promociones, "/admin/promociones"), (Modulos.Usuarios, "/admin/usuarios"), (Modulos.Resenas, "/admin/resenas"),
        (Modulos.Mensajes, "/admin/mensajes"), (Modulos.Configuracion, "/admin/configuracion")
    ];
}
