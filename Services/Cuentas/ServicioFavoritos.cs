using System.Security.Claims;
using LlamitasStore.Data;
using LlamitasStore.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace LlamitasStore.Services.Cuentas;

public record ResultadoFavorito(bool Encontrado, bool EsFavorito, int Total);

public class ServicioFavoritos
{
    private readonly AppDbContext _db;
    public ServicioFavoritos(AppDbContext db) => _db = db;

    /// <summary>Agrega o quita. Solo se pueden agregar productos visibles (activos o descontinuados).</summary>
    public async Task<ResultadoFavorito> AlternarAsync(int usuarioId, int productoId, CancellationToken ct = default)
    {
        var existente = await _db.Favoritos.FirstOrDefaultAsync(f => f.UsuarioId == usuarioId && f.ProductoId == productoId, ct);
        if (existente is not null)
        {
            _db.Favoritos.Remove(existente);
            await _db.SaveChangesAsync(ct);
            return new ResultadoFavorito(true, false, await ContarAsync(usuarioId, ct));
        }

        var visible = await _db.Productos.AnyAsync(p => p.Id == productoId && p.Estado != EstadoProducto.Inactivo, ct);
        if (!visible) return new ResultadoFavorito(false, false, await ContarAsync(usuarioId, ct));

        _db.Favoritos.Add(new Favorito { UsuarioId = usuarioId, ProductoId = productoId });
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Doble clic simultáneo: la clave primaria (usuario, producto) ya lo registró.
            _db.ChangeTracker.Clear();
        }
        return new ResultadoFavorito(true, true, await ContarAsync(usuarioId, ct));
    }

    public Task<int> ContarAsync(int usuarioId, CancellationToken ct = default) =>
        _db.Favoritos.CountAsync(f => f.UsuarioId == usuarioId && f.Producto.Estado != EstadoProducto.Inactivo, ct);

    /// <summary>Ids de favoritos, del más reciente al más antiguo.</summary>
    public Task<List<int>> ListarIdsAsync(int usuarioId, CancellationToken ct = default) =>
        _db.Favoritos.AsNoTracking()
            .Where(f => f.UsuarioId == usuarioId && f.Producto.Estado != EstadoProducto.Inactivo)
            .OrderByDescending(f => f.Fecha)
            .Select(f => f.ProductoId)
            .ToListAsync(ct);
}

/// <summary>Favoritos del usuario actual, cargados una sola vez por petición (para pintar los corazones de las tarjetas).</summary>
public class FavoritosDelUsuario
{
    private readonly IHttpContextAccessor _http;
    private readonly ServicioFavoritos _favoritos;
    private HashSet<int>? _ids;

    public FavoritosDelUsuario(IHttpContextAccessor http, ServicioFavoritos favoritos)
    {
        _http = http;
        _favoritos = favoritos;
    }

    public bool Autenticado => _http.HttpContext?.User.Identity?.IsAuthenticated == true;

    public async Task<IReadOnlySet<int>> IdsAsync()
    {
        if (_ids is not null) return _ids;
        var id = _http.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
        _ids = int.TryParse(id, out var usuarioId)
            ? (await _favoritos.ListarIdsAsync(usuarioId)).ToHashSet()
            : [];
        return _ids;
    }
}
