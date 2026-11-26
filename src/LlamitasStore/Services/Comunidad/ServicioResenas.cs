using LlamitasStore.Data;
using LlamitasStore.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace LlamitasStore.Services.Comunidad;

public enum ResultadoResena { Creada, Actualizada, ProductoNoDisponible, Invalida }

/// <summary>Datos que escribe el cliente. Se validan aquí aunque el formulario ya los valide.</summary>
public record DatosResena(int Calificacion, string? Titulo, string? Comentario);

/// <summary>Reseña propia del usuario sobre un producto (para precargar el formulario).</summary>
public record ResenaPropia(int Calificacion, string Titulo, string Comentario, bool Aprobada, bool CompraVerificada);

/// <summary>
/// Reseñas de clientes: una por usuario y producto (se edita, no se duplica).
/// "Compra verificada" se calcula en el servidor: el usuario tiene un pedido entregado con ese producto.
/// Si la moderación ocultó la reseña, editarla no la vuelve a publicar.
/// </summary>
public class ServicioResenas
{
    public const int TituloMin = 3, TituloMax = 100, ComentarioMin = 10, ComentarioMax = 2000;

    private readonly AppDbContext _db;
    private readonly TimeProvider _reloj;

    public ServicioResenas(AppDbContext db, TimeProvider reloj)
    {
        _db = db;
        _reloj = reloj;
    }

    public static string? Validar(DatosResena d)
    {
        if (d.Calificacion is < 1 or > 5) return "Elige de 1 a 5 estrellas.";
        var titulo = d.Titulo?.Trim() ?? "";
        var comentario = d.Comentario?.Trim() ?? "";
        if (titulo.Length is < TituloMin or > TituloMax) return $"El título debe tener entre {TituloMin} y {TituloMax} caracteres.";
        if (comentario.Length is < ComentarioMin or > ComentarioMax) return $"El comentario debe tener entre {ComentarioMin} y {ComentarioMax} caracteres.";
        return null;
    }

    public Task<bool> CompraVerificadaAsync(int usuarioId, int productoId, CancellationToken ct = default) =>
        _db.Pedidos.AnyAsync(p => p.UsuarioId == usuarioId && p.Estado == EstadoPedido.Entregado
                                  && p.Detalles.Any(d => d.ProductoId == productoId), ct);

    public async Task<ResenaPropia?> ObtenerPropiaAsync(int usuarioId, int productoId, CancellationToken ct = default) =>
        await _db.Resenas.AsNoTracking()
            .Where(r => r.UsuarioId == usuarioId && r.ProductoId == productoId)
            .Select(r => new ResenaPropia(r.Calificacion, r.Titulo, r.Comentario, r.Aprobada, r.CompraVerificada))
            .FirstOrDefaultAsync(ct);

    public async Task<(ResultadoResena Resultado, string? Error)> GuardarAsync(int usuarioId, int productoId, DatosResena datos, CancellationToken ct = default)
    {
        if (Validar(datos) is { } error) return (ResultadoResena.Invalida, error);
        // Se pueden reseñar productos visibles en la tienda (activos o descontinuados), no los inactivos.
        if (!await _db.Productos.AnyAsync(p => p.Id == productoId && p.Estado != EstadoProducto.Inactivo, ct))
            return (ResultadoResena.ProductoNoDisponible, "El producto no está disponible.");

        var verificada = await CompraVerificadaAsync(usuarioId, productoId, ct);
        var ahora = _reloj.GetUtcNow().UtcDateTime;
        var r = await _db.Resenas.FirstOrDefaultAsync(x => x.UsuarioId == usuarioId && x.ProductoId == productoId, ct);
        var nueva = r is null;
        if (r is null)
        {
            r = new Resena { UsuarioId = usuarioId, ProductoId = productoId, Aprobada = true };
            _db.Resenas.Add(r);
        }
        r.Calificacion = datos.Calificacion;
        r.Titulo = datos.Titulo!.Trim();
        r.Comentario = datos.Comentario!.Trim();
        r.CompraVerificada = verificada;
        r.Fecha = ahora;

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) when (nueva)
        {
            // Doble envío simultáneo: el índice único (producto, usuario) impidió el duplicado.
            // Se reintenta una sola vez como edición; cualquier otro error se propaga.
            _db.ChangeTracker.Clear();
            if (!await _db.Resenas.AnyAsync(x => x.UsuarioId == usuarioId && x.ProductoId == productoId, ct)) throw;
            return await GuardarAsync(usuarioId, productoId, datos, ct);
        }
        await RecalcularAsync(_db, productoId, ct);
        return (nueva ? ResultadoResena.Creada : ResultadoResena.Actualizada, null);
    }

    public async Task<bool> EliminarAsync(int usuarioId, int productoId, CancellationToken ct = default)
    {
        var borradas = await _db.Resenas.Where(r => r.UsuarioId == usuarioId && r.ProductoId == productoId).ExecuteDeleteAsync(ct);
        if (borradas > 0) await RecalcularAsync(_db, productoId, ct);
        return borradas > 0;
    }

    /// <summary>Promedio y total del producto con las reseñas visibles. Lo usan la tienda y la moderación.</summary>
    public static async Task RecalcularAsync(AppDbContext db, int productoId, CancellationToken ct = default)
    {
        var visibles = await db.Resenas.Where(x => x.ProductoId == productoId && x.Aprobada).Select(x => x.Calificacion).ToListAsync(ct);
        var promedio = visibles.Count == 0 ? 0m : Math.Round((decimal)visibles.Average(), 2);
        await db.Productos.Where(p => p.Id == productoId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.ValoracionPromedio, promedio).SetProperty(p => p.TotalResenas, visibles.Count), ct);
    }
}
