using System.Globalization;
using LlamitasStore.Data;
using LlamitasStore.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace LlamitasStore.Services.Ventas;

public record ValidacionCupon(CuponAplicable? Cupon, string? Error)
{
    public bool Valido => Cupon is not null;
}

public class ServicioCupones
{
    private static readonly CultureInfo Bo = CultureInfo.GetCultureInfo("es-BO");
    private readonly AppDbContext _db;
    private readonly TimeProvider _reloj;

    public ServicioCupones(AppDbContext db, TimeProvider reloj)
    {
        _db = db;
        _reloj = reloj;
    }

    public static string Normalizar(string? codigo) => (codigo ?? string.Empty).Trim().ToUpperInvariant();

    /// <param name="usuarioId">null para visitantes: el límite por usuario se comprueba al confirmar el pedido.</param>
    public async Task<ValidacionCupon> ValidarAsync(string? codigo, int? usuarioId, decimal subtotal, CancellationToken ct = default)
    {
        var normalizado = Normalizar(codigo);
        if (normalizado.Length == 0 || normalizado.Length > 40) return new(null, "Escribe un código de cupón.");

        var cupon = await _db.Cupones.AsNoTracking().FirstOrDefaultAsync(c => c.Codigo == normalizado, ct);
        var ahora = _reloj.GetUtcNow().UtcDateTime;

        if (cupon is null || !cupon.Activo) return new(null, "El cupón no existe o ya no está activo.");
        if (ahora < cupon.FechaInicio) return new(null, "El cupón todavía no está vigente.");
        if (ahora >= cupon.FechaFin) return new(null, "El cupón ya venció.");
        if (cupon.UsosMaximos is not null && cupon.UsosActuales >= cupon.UsosMaximos) return new(null, "El cupón ya alcanzó su límite de usos.");
        if (subtotal < cupon.MontoMinimo)
            return new(null, $"El cupón requiere una compra mínima de Bs. {cupon.MontoMinimo.ToString("N2", Bo)}.");

        if (usuarioId is not null && cupon.UsosPorUsuario is not null)
        {
            var usos = await _db.Pedidos.CountAsync(p => p.UsuarioId == usuarioId && p.CuponId == cupon.Id && p.Estado != EstadoPedido.Cancelado, ct);
            if (usos >= cupon.UsosPorUsuario) return new(null, "Ya usaste este cupón el máximo de veces permitido.");
        }

        return new(new CuponAplicable(cupon.Id, cupon.Codigo, cupon.Tipo, cupon.Valor), null);
    }
}
