using LlamitasStore.Models.Entities;

namespace LlamitasStore.Services.Ventas;

public record LineaCalculo(decimal PrecioUnitario, int Cantidad)
{
    public decimal Subtotal => Math.Round(PrecioUnitario * Cantidad, 2, MidpointRounding.AwayFromZero);
}

public record CuponAplicable(int Id, string Codigo, TipoDescuento Tipo, decimal Valor);

public record Totales(decimal Subtotal, decimal Descuento, decimal CostoEnvio, decimal Total, decimal EnvioGratisDesde)
{
    /// <summary>Cuánto falta para el envío gratis (0 si ya aplica o si no hay envío gratis configurado).</summary>
    public decimal FaltaParaEnvioGratis =>
        EnvioGratisDesde <= 0 || CostoEnvio == 0 ? 0 : Math.Max(0, EnvioGratisDesde - (Subtotal - Descuento));
}

/// <summary>
/// ÚNICA fórmula de totales (carrito, checkout y pedido guardado usan esta misma función):
///   subtotal  = Σ precio final × cantidad (precio final ya incluye oferta o promoción)
///   descuento = cupón: porcentaje del subtotal, o monto fijo (nunca mayor al subtotal)
///   envío     = gratis si (subtotal − descuento) ≥ umbral configurado; si no, costo fijo
///   total     = subtotal − descuento + envío
/// </summary>
public static class CalculadoraTotales
{
    public static Totales Calcular(IEnumerable<LineaCalculo> lineas, CuponAplicable? cupon, decimal costoEnvio, decimal envioGratisDesde)
    {
        var lista = lineas.ToList();
        var subtotal = lista.Sum(l => l.Subtotal);
        if (lista.Count == 0) return new Totales(0, 0, 0, 0, envioGratisDesde);

        var descuento = cupon is null ? 0 : cupon.Tipo switch
        {
            TipoDescuento.Porcentaje => Math.Round(subtotal * Math.Clamp(cupon.Valor, 0, 100) / 100m, 2, MidpointRounding.AwayFromZero),
            TipoDescuento.MontoFijo => Math.Min(cupon.Valor, subtotal),
            _ => 0
        };

        var baseEnvio = subtotal - descuento;
        var envio = envioGratisDesde > 0 && baseEnvio >= envioGratisDesde ? 0 : Math.Max(0, costoEnvio);

        return new Totales(subtotal, descuento, envio, subtotal - descuento + envio, envioGratisDesde);
    }
}
