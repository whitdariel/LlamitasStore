using LlamitasStore.Models.Entities;
using LlamitasStore.Services.Ventas;

namespace LlamitasStore.Tests.Ventas;

public class CalculadoraTotalesTests
{
    private static Totales Calc(decimal[] precios, int[] cantidades, CuponAplicable? cupon = null, decimal envio = 25, decimal gratisDesde = 1500) =>
        CalculadoraTotales.Calcular(precios.Zip(cantidades, (p, c) => new LineaCalculo(p, c)), cupon, envio, gratisDesde);

    [Fact]
    public void Subtotal_envio_y_total_sin_cupon()
    {
        var t = Calc([331.50m, 890m], [2, 1]);
        Assert.Equal(1553.00m, t.Subtotal);   // 663 + 890
        Assert.Equal(0m, t.CostoEnvio);         // supera 1500
        Assert.Equal(1553.00m, t.Total);
    }

    [Fact]
    public void Cobra_envio_bajo_el_umbral_y_calcula_cuanto_falta()
    {
        var t = Calc([390m], [1]);
        Assert.Equal(25m, t.CostoEnvio);
        Assert.Equal(415m, t.Total);
        Assert.Equal(1110m, t.FaltaParaEnvioGratis);
    }

    [Fact]
    public void Cupon_porcentual_se_redondea_a_centavos()
    {
        var t = Calc([333.33m], [1], new CuponAplicable(1, "P10", TipoDescuento.Porcentaje, 10));
        Assert.Equal(33.33m, t.Descuento);
        Assert.Equal(333.33m - 33.33m + 25m, t.Total);
    }

    [Fact]
    public void Cupon_de_monto_fijo_nunca_supera_el_subtotal()
    {
        var t = Calc([30m], [1], new CuponAplicable(1, "F50", TipoDescuento.MontoFijo, 50));
        Assert.Equal(30m, t.Descuento);
        Assert.Equal(25m, t.Total); // solo el envío
    }

    [Fact]
    public void El_envio_gratis_se_evalua_despues_del_descuento()
    {
        // 1600 − 10% = 1440 < 1500 → paga envío.
        var t = Calc([1600m], [1], new CuponAplicable(1, "P10", TipoDescuento.Porcentaje, 10));
        Assert.Equal(160m, t.Descuento);
        Assert.Equal(25m, t.CostoEnvio);
        Assert.Equal(1465m, t.Total);
    }

    [Fact]
    public void Umbral_cero_desactiva_el_envio_gratis()
    {
        var t = Calc([5000m], [1], gratisDesde: 0);
        Assert.Equal(25m, t.CostoEnvio);
        Assert.Equal(0m, t.FaltaParaEnvioGratis);
    }

    [Fact]
    public void Carrito_vacio_no_cobra_envio()
    {
        var t = CalculadoraTotales.Calcular([], null, 25, 1500);
        Assert.Equal((0m, 0m, 0m), (t.Subtotal, t.CostoEnvio, t.Total));
    }
}
