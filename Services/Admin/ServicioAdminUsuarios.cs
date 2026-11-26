using LlamitasStore.Data;
using LlamitasStore.Infraestructura;
using LlamitasStore.Models.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace LlamitasStore.Services.Admin;

public record FilaPermiso(int ModuloId, string Clave, string Nombre, bool Leer, bool Crear, bool Editar, bool Eliminar);

/// <summary>Usuarios, roles y la matriz de permisos (heredada del proyecto original). Reglas de protección incluidas.</summary>
public class ServicioAdminUsuarios
{
    private readonly AppDbContext _db;
    private readonly UserManager<Usuario> _usuarios;
    private readonly RoleManager<Rol> _roles;

    public ServicioAdminUsuarios(AppDbContext db, UserManager<Usuario> usuarios, RoleManager<Rol> roles)
    {
        _db = db;
        _usuarios = usuarios;
        _roles = roles;
    }

    /// <summary>
    /// Cambia el rol (uno por usuario). Reglas: no cambiarse el propio rol y no dejar la tienda sin administradores activos.
    /// </summary>
    public async Task<string?> CambiarRolAsync(int usuarioId, string rolNuevo, int actorId)
    {
        if (usuarioId == actorId) return "No puedes cambiar tu propio rol.";
        var usuario = await _usuarios.FindByIdAsync(usuarioId.ToString());
        if (usuario is null) return "El usuario no existe.";
        if (!await _roles.RoleExistsAsync(rolNuevo)) return "El rol no existe.";

        var actuales = await _usuarios.GetRolesAsync(usuario);
        if (actuales.Count == 1 && actuales[0] == rolNuevo) return null;
        if (actuales.Contains(Roles.Administrador) && rolNuevo != Roles.Administrador && await AdminsActivosAsync() <= 1)
            return "No puedes quitar el rol al último administrador activo.";

        var quitar = await _usuarios.RemoveFromRolesAsync(usuario, actuales);
        if (!quitar.Succeeded) return string.Join(" ", quitar.Errors.Select(e => e.Description));
        var poner = await _usuarios.AddToRoleAsync(usuario, rolNuevo);
        if (!poner.Succeeded) return string.Join(" ", poner.Errors.Select(e => e.Description));

        await _usuarios.UpdateSecurityStampAsync(usuario); // la sesión abierta se renueva con el rol nuevo (≤ 5 min)
        return null;
    }

    /// <summary>Activa, desactiva o suspende. Al dejar de estar activo se invalida su sesión (SecurityStamp).</summary>
    public async Task<string?> CambiarEstadoAsync(int usuarioId, EstadoUsuario estado, int actorId)
    {
        if (usuarioId == actorId) return "No puedes cambiar el estado de tu propia cuenta.";
        var usuario = await _usuarios.FindByIdAsync(usuarioId.ToString());
        if (usuario is null) return "El usuario no existe.";
        if (estado != EstadoUsuario.Activo && usuario.Estado == EstadoUsuario.Activo
            && await _usuarios.IsInRoleAsync(usuario, Roles.Administrador) && await AdminsActivosAsync() <= 1)
            return "No puedes desactivar al último administrador activo.";

        usuario.Estado = estado;
        var r = await _usuarios.UpdateAsync(usuario);
        if (!r.Succeeded) return string.Join(" ", r.Errors.Select(e => e.Description));
        await _usuarios.UpdateSecurityStampAsync(usuario);
        return null;
    }

    private async Task<int> AdminsActivosAsync() =>
        (await _usuarios.GetUsersInRoleAsync(Roles.Administrador)).Count(u => u.Estado == EstadoUsuario.Activo);

    public async Task<string?> CrearRolAsync(string nombre, string? descripcion)
    {
        nombre = nombre.Trim();
        if (nombre.Length is < 3 or > 50) return "El nombre del rol debe tener entre 3 y 50 caracteres.";
        if (await _roles.RoleExistsAsync(nombre)) return "Ya existe un rol con ese nombre.";
        var r = await _roles.CreateAsync(new Rol(nombre, descripcion?.Trim()));
        return r.Succeeded ? null : string.Join(" ", r.Errors.Select(e => e.Description));
    }

    public async Task<string?> EliminarRolAsync(int rolId)
    {
        var rol = await _roles.FindByIdAsync(rolId.ToString());
        if (rol is null) return "El rol no existe.";
        if (rol.EsSistema) return "Los roles del sistema no se pueden eliminar.";
        if ((await _usuarios.GetUsersInRoleAsync(rol.Name!)).Count > 0) return "El rol tiene usuarios asignados: cámbialos de rol primero.";
        var r = await _roles.DeleteAsync(rol);
        return r.Succeeded ? null : string.Join(" ", r.Errors.Select(e => e.Description));
    }

    public async Task<List<FilaPermiso>> MatrizAsync(int rolId)
    {
        var modulos = await _db.Modulos.AsNoTracking().OrderBy(m => m.Orden).ToListAsync();
        var permisos = await _db.Permisos.AsNoTracking().Where(p => p.RolId == rolId).ToDictionaryAsync(p => p.ModuloId);
        return modulos.Select(m =>
        {
            permisos.TryGetValue(m.Id, out var p);
            return new FilaPermiso(m.Id, m.Clave, m.Nombre, p?.PuedeLeer ?? false, p?.PuedeCrear ?? false, p?.PuedeEditar ?? false, p?.PuedeEliminar ?? false);
        }).ToList();
    }

    /// <summary>
    /// Guarda la matriz de un rol. El Administrador no se edita (siempre tiene todo).
    /// Crear/editar/eliminar implican leer.
    /// </summary>
    public async Task<string?> GuardarMatrizAsync(int rolId, IReadOnlyList<FilaPermiso> filas)
    {
        var rol = await _roles.FindByIdAsync(rolId.ToString());
        if (rol is null) return "El rol no existe.";
        if (rol.Name == Roles.Administrador) return "Los permisos del Administrador son fijos.";

        var modulosValidos = await _db.Modulos.Select(m => m.Id).ToListAsync();
        var existentes = await _db.Permisos.Where(p => p.RolId == rolId).ToDictionaryAsync(p => p.ModuloId);
        foreach (var f in filas.Where(f => modulosValidos.Contains(f.ModuloId)))
        {
            var leer = f.Leer || f.Crear || f.Editar || f.Eliminar;
            if (!existentes.TryGetValue(f.ModuloId, out var p))
            {
                if (!leer) continue;
                p = new Permiso { RolId = rolId, ModuloId = f.ModuloId };
                _db.Permisos.Add(p);
            }
            (p.PuedeLeer, p.PuedeCrear, p.PuedeEditar, p.PuedeEliminar) = (leer, f.Crear, f.Editar, f.Eliminar);
        }
        await _db.SaveChangesAsync();
        return null;
    }
}
