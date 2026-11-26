using LlamitasStore.Areas.Admin.Models;
using LlamitasStore.Data;
using LlamitasStore.Infraestructura;
using LlamitasStore.Models.Entities;
using LlamitasStore.Services.Admin;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LlamitasStore.Areas.Admin.Controllers;

[Route("admin/usuarios")]
public class UsuariosController : AdminController
{
    private readonly AppDbContext _db;
    private readonly ServicioAdminUsuarios _usuarios;

    public UsuariosController(AppDbContext db, ServicioAdminUsuarios usuarios)
    {
        _db = db;
        _usuarios = usuarios;
    }

    public record FilaUsuario(int Id, string Nombre, string Email, string? Rol, EstadoUsuario Estado, DateTime Alta, DateTime? UltimoAcceso, int Pedidos);

    [HttpGet("")]
    [Permiso(Modulos.Usuarios)]
    public async Task<IActionResult> Index(string? q, string? rol, int pagina = 1, CancellationToken ct = default)
    {
        var consulta = from u in _db.Users.AsNoTracking()
                       let nombreRol = (from ur in _db.UserRoles join r in _db.Roles on ur.RoleId equals r.Id where ur.UserId == u.Id select r.Name).FirstOrDefault()
                       select new { u, nombreRol };
        if (!string.IsNullOrWhiteSpace(q))
        {
            var t = $"%{q.Trim()}%";
            consulta = consulta.Where(x => EF.Functions.Like(x.u.Email!, t) || EF.Functions.Like(x.u.Nombre + " " + x.u.Apellido, t));
        }
        if (!string.IsNullOrWhiteSpace(rol)) consulta = consulta.Where(x => x.nombreRol == rol);

        var (actual, saltar) = Paginar(pagina);
        var total = await consulta.CountAsync(ct);
        var filas = await consulta.OrderByDescending(x => x.u.FechaCreacion).Skip(saltar).Take(TamanoPagina)
            .Select(x => new FilaUsuario(x.u.Id, x.u.Nombre + " " + x.u.Apellido, x.u.Email!, x.nombreRol, x.u.Estado, x.u.FechaCreacion, x.u.UltimoAcceso,
                _db.Pedidos.Count(p => p.UsuarioId == x.u.Id)))
            .ToListAsync(ct);
        (ViewData["Q"], ViewData["Rol"]) = (q, rol);
        ViewData["Roles"] = await _db.Roles.OrderBy(r => r.Name).Select(r => r.Name!).ToListAsync(ct);
        return View(new Pagina<FilaUsuario>(filas, total, actual));
    }

    [HttpPost("{id:int}/rol")]
    [Permiso(Modulos.Usuarios, P.Editar)]
    public async Task<IActionResult> CambiarRol(int id, string rol) =>
        Resultado(await _usuarios.CambiarRolAsync(id, rol, UsuarioId), $"Rol actualizado a {rol}.", nameof(Index));

    [HttpPost("{id:int}/estado")]
    [Permiso(Modulos.Usuarios, P.Editar)]
    public async Task<IActionResult> CambiarEstado(int id, EstadoUsuario estado) =>
        Resultado(await _usuarios.CambiarEstadoAsync(id, estado, UsuarioId), $"Cuenta marcada como {estado}.", nameof(Index));

    // ---------- Roles y permisos (matriz heredada del proyecto original) ----------

    [HttpGet("roles")]
    [Permiso(Modulos.Usuarios)]
    public async Task<IActionResult> Roles(CancellationToken ct) =>
        View(await _db.Roles.AsNoTracking().OrderBy(r => r.Id)
            .Select(r => new FilaRol(r.Id, r.Name!, r.Descripcion, r.EsSistema, _db.UserRoles.Count(ur => ur.RoleId == r.Id)))
            .ToListAsync(ct));

    public record FilaRol(int Id, string Nombre, string? Descripcion, bool EsSistema, int Usuarios);

    [HttpPost("roles")]
    [Permiso(Modulos.Usuarios, P.Crear)]
    public async Task<IActionResult> CrearRol(string nombre, string? descripcion) =>
        Resultado(await _usuarios.CrearRolAsync(nombre ?? string.Empty, descripcion), "Rol creado. Ahora asígnale permisos.", nameof(Roles));

    [HttpPost("roles/{id:int}/eliminar")]
    [Permiso(Modulos.Usuarios, P.Eliminar)]
    public async Task<IActionResult> EliminarRol(int id) =>
        Resultado(await _usuarios.EliminarRolAsync(id), "Rol eliminado.", nameof(Roles));

    [HttpGet("roles/{id:int}/permisos")]
    [Permiso(Modulos.Usuarios)]
    public async Task<IActionResult> Permisos(int id, CancellationToken ct)
    {
        var rol = await _db.Roles.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
        if (rol is null) return NotFound();
        ViewData["Rol"] = rol;
        return View(await _usuarios.MatrizAsync(id));
    }

    [HttpPost("roles/{id:int}/permisos")]
    [Permiso(Modulos.Usuarios, P.Editar)]
    public async Task<IActionResult> Permisos(int id, int[] moduloId, string[]? leer, string[]? crear, string[]? editar, string[]? eliminar)
    {
        bool Marcado(string[]? lista, int m) => lista?.Contains(m.ToString()) == true;
        var filas = moduloId.Select(m => new FilaPermiso(m, "", "", Marcado(leer, m), Marcado(crear, m), Marcado(editar, m), Marcado(eliminar, m))).ToList();
        return Resultado(await _usuarios.GuardarMatrizAsync(id, filas), "Permisos guardados.", nameof(Permisos), new { id });
    }
}
