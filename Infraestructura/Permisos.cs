using System.Security.Claims;
using LlamitasStore.Data;
using LlamitasStore.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LlamitasStore.Infraestructura;

public enum AccionPermiso { Leer, Crear, Editar, Eliminar }

/// <summary>[Permiso(Modulos.Productos, AccionPermiso.Editar)] → política "Permiso:productos:Editar", evaluada en el servidor.</summary>
public sealed class PermisoAttribute : AuthorizeAttribute
{
    public const string Prefijo = "Permiso:";
    public PermisoAttribute(string modulo, AccionPermiso accion = AccionPermiso.Leer) => Policy = $"{Prefijo}{modulo}:{accion}";
}

public sealed record PermisoRequisito(string Modulo, AccionPermiso Accion) : IAuthorizationRequirement;

/// <summary>Crea las políticas "Permiso:modulo:accion" bajo demanda (no hay que registrarlas una por una).</summary>
public sealed class ProveedorPoliticasPermiso : DefaultAuthorizationPolicyProvider
{
    public ProveedorPoliticasPermiso(IOptions<AuthorizationOptions> opciones) : base(opciones) { }

    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string nombre)
    {
        if (!nombre.StartsWith(PermisoAttribute.Prefijo, StringComparison.Ordinal)) return await base.GetPolicyAsync(nombre);

        var partes = nombre[PermisoAttribute.Prefijo.Length..].Split(':');
        if (partes.Length != 2 || !Enum.TryParse<AccionPermiso>(partes[1], out var accion)) return null;

        return new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new PermisoRequisito(partes[0], accion))
            .Build();
    }
}

public sealed class ManejadorPermiso : AuthorizationHandler<PermisoRequisito>
{
    private readonly ServicioPermisos _permisos;
    public ManejadorPermiso(ServicioPermisos permisos) => _permisos = permisos;

    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext contexto, PermisoRequisito requisito)
    {
        if (await _permisos.TieneAsync(contexto.User, requisito.Modulo, requisito.Accion)) contexto.Succeed(requisito);
    }
}

/// <summary>
/// Consulta la matriz rol × módulo (tabla permiso). El Administrador siempre tiene todo.
/// Los permisos del usuario se cargan una vez por petición (también los usa el menú del panel).
/// </summary>
public sealed class ServicioPermisos
{
    private readonly AppDbContext _db;
    private Dictionary<string, (bool Leer, bool Crear, bool Editar, bool Eliminar)>? _cache;
    private string? _usuarioCache;

    public ServicioPermisos(AppDbContext db) => _db = db;

    public async Task<bool> TieneAsync(ClaimsPrincipal usuario, string modulo, AccionPermiso accion)
    {
        if (usuario.Identity?.IsAuthenticated != true) return false;
        if (usuario.IsInRole(Roles.Administrador)) return true;

        var matriz = await MatrizAsync(usuario);
        if (!matriz.TryGetValue(modulo, out var p)) return false;
        return accion switch
        {
            AccionPermiso.Leer => p.Leer,
            AccionPermiso.Crear => p.Crear,
            AccionPermiso.Editar => p.Editar,
            AccionPermiso.Eliminar => p.Eliminar,
            _ => false
        };
    }

    public async Task<bool> PuedeEntrarAlPanelAsync(ClaimsPrincipal usuario)
    {
        if (usuario.Identity?.IsAuthenticated != true) return false;
        if (usuario.IsInRole(Roles.Administrador)) return true;
        return (await MatrizAsync(usuario)).Values.Any(p => p.Leer);
    }

    private async Task<Dictionary<string, (bool Leer, bool Crear, bool Editar, bool Eliminar)>> MatrizAsync(ClaimsPrincipal usuario)
    {
        var id = usuario.FindFirstValue(ClaimTypes.NameIdentifier);
        if (_cache is not null && _usuarioCache == id) return _cache;

        var roles = usuario.FindAll(ClaimTypes.Role).Select(r => r.Value).ToList();
        var filas = await _db.Permisos.AsNoTracking()
            .Where(p => roles.Contains(p.Rol.Name!))
            .Select(p => new { p.Modulo.Clave, p.PuedeLeer, p.PuedeCrear, p.PuedeEditar, p.PuedeEliminar })
            .ToListAsync();

        // Un usuario con varios roles suma permisos (OR).
        _cache = filas.GroupBy(f => f.Clave).ToDictionary(g => g.Key, g => (
            g.Any(x => x.PuedeLeer), g.Any(x => x.PuedeCrear), g.Any(x => x.PuedeEditar), g.Any(x => x.PuedeEliminar)));
        _usuarioCache = id;
        return _cache;
    }
}
