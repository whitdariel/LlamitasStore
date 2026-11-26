using LlamitasStore.Data;
using Microsoft.EntityFrameworkCore;

namespace LlamitasStore.Services.Comunidad;

public record NotificacionVista(int Id, string Titulo, string Mensaje, string? Url, bool Leida, DateTime Fecha);

/// <summary>Notificaciones del cliente (cambios de estado y pagos de sus pedidos). Siempre filtradas por el dueño.</summary>
public class ServicioNotificaciones
{
    public const int Maximo = 50;
    private readonly AppDbContext _db;

    public ServicioNotificaciones(AppDbContext db) => _db = db;

    public Task<int> ContarNoLeidasAsync(int usuarioId, CancellationToken ct = default) =>
        _db.Notificaciones.CountAsync(n => n.UsuarioId == usuarioId && !n.Leida, ct);

    public Task<List<NotificacionVista>> ListarAsync(int usuarioId, CancellationToken ct = default) =>
        _db.Notificaciones.AsNoTracking()
            .Where(n => n.UsuarioId == usuarioId)
            .OrderByDescending(n => n.Fecha).ThenByDescending(n => n.Id)
            .Take(Maximo)
            .Select(n => new NotificacionVista(n.Id, n.Titulo, n.Mensaje, n.Url, n.Leida, n.Fecha))
            .ToListAsync(ct);

    /// <summary>Marca como leída y devuelve su URL. null si no existe o es de otro usuario.</summary>
    public async Task<(bool Encontrada, string? Url)> AbrirAsync(int usuarioId, int id, CancellationToken ct = default)
    {
        var n = await _db.Notificaciones.FirstOrDefaultAsync(x => x.Id == id && x.UsuarioId == usuarioId, ct);
        if (n is null) return (false, null);
        if (!n.Leida)
        {
            n.Leida = true;
            await _db.SaveChangesAsync(ct);
        }
        return (true, n.Url);
    }

    public Task<int> MarcarTodasAsync(int usuarioId, CancellationToken ct = default) =>
        _db.Notificaciones.Where(n => n.UsuarioId == usuarioId && !n.Leida)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.Leida, true), ct);
}
