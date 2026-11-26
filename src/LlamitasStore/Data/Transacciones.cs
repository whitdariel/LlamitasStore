using Microsoft.EntityFrameworkCore;

namespace LlamitasStore.Data;

public static class Transacciones
{
    /// <summary>
    /// Ejecuta <paramref name="accion"/> en una transacción compatible con la estrategia de reintentos de MySQL
    /// (EnableRetryOnFailure no permite BeginTransaction directo). Si hay un fallo transitorio, se reintenta
    /// TODA la acción: debe cargar sus datos dentro de la lambda.
    /// </summary>
    public static Task<T> EnTransaccionAsync<T>(this AppDbContext db, Func<CancellationToken, Task<T>> accion, CancellationToken ct = default)
    {
        var estrategia = db.Database.CreateExecutionStrategy();
        return estrategia.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var resultado = await accion(ct);
            await tx.CommitAsync(ct);
            return resultado;
        });
    }
}
