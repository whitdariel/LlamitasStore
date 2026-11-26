using LlamitasStore.Models.Entities;
using LlamitasStore.Services.Catalogo;
using LlamitasStore.Tests.Soporte;

namespace LlamitasStore.Tests.Catalogo;

public class ServicioPreciosTests
{
    private static readonly PromocionVigente[] SinPromos = [];

    [Fact]
    public void Sin_descuentos_el_precio_final_es_el_de_lista()
    {
        var p = ServicioPrecios.Calcular(1000m, null, 1, 1, 1, SinPromos);

        Assert.Equal(1000m, p.PrecioFinal);
        Assert.False(p.TieneDescuento);
        Assert.Equal(0, p.PorcentajeDescuento);
        Assert.Null(p.Motivo);
    }

    [Fact]
    public void Precio_de_oferta_se_aplica_y_calcula_ahorro_y_porcentaje()
    {
        var p = ServicioPrecios.Calcular(1690m, 1590m, 1, 1, 1, SinPromos);

        Assert.Equal(1590m, p.PrecioFinal);
        Assert.Equal(100m, p.Ahorro);
        Assert.Equal(6, p.PorcentajeDescuento); // 5.9% redondeado
        Assert.Equal("Oferta", p.Motivo);
    }

    [Fact]
    public void Oferta_mayor_o_igual_al_precio_se_ignora()
    {
        Assert.Equal(1000m, ServicioPrecios.Calcular(1000m, 1000m, 1, 1, 1, SinPromos).PrecioFinal);
        Assert.Equal(1000m, ServicioPrecios.Calcular(1000m, 1200m, 1, 1, 1, SinPromos).PrecioFinal);
    }

    [Fact]
    public void Se_elige_el_menor_entre_oferta_y_promocion_sin_acumular()
    {
        var promo = new PromocionVigente(1, "Semana gamer", 15m, null, null, CategoriaId: 7);

        // Oferta 900 (10%) vs promo 15% = 850 → gana la promo.
        var conPromo = ServicioPrecios.Calcular(1000m, 900m, 1, 2, 7, [promo]);
        Assert.Equal(850m, conPromo.PrecioFinal);
        Assert.Equal("Semana gamer", conPromo.Motivo);

        // Oferta 800 (20%) vs promo 15% = 850 → gana la oferta. Nunca 1000 * 0.8 * 0.85.
        var conOferta = ServicioPrecios.Calcular(1000m, 800m, 1, 2, 7, [promo]);
        Assert.Equal(800m, conOferta.PrecioFinal);
        Assert.Equal("Oferta", conOferta.Motivo);
    }

    [Theory]
    [InlineData(5, null, null, true)]    // por producto
    [InlineData(null, 3, null, true)]    // por marca
    [InlineData(null, null, 9, true)]    // por categoría
    [InlineData(6, 4, 8, false)]         // no coincide nada
    public void Promocion_aplica_por_producto_marca_o_categoria(int? productoId, int? marcaId, int? categoriaId, bool aplica)
    {
        var promo = new PromocionVigente(1, "Promo", 10m, productoId, marcaId, categoriaId);
        var p = ServicioPrecios.Calcular(500m, null, productoId: 5, marcaId: 3, categoriaId: 9, [promo]);
        Assert.Equal(aplica ? 450m : 500m, p.PrecioFinal);
    }

    [Fact]
    public void Precio_con_promocion_se_redondea_a_centavos()
    {
        var promo = new PromocionVigente(1, "Promo", 15m, 1, null, null);
        Assert.Equal(330.65m, ServicioPrecios.Calcular(389m, null, 1, 1, 1, [promo]).PrecioFinal); // 389 * 0.85 = 330.65
        Assert.Equal(282.62m, ServicioPrecios.Calcular(332.49m, null, 1, 1, 1, [promo]).PrecioFinal); // 282.6165 → 282.62
    }

    [Fact]
    public async Task Solo_se_cargan_promociones_activas_y_dentro_de_su_vigencia()
    {
        await using var bd = await BaseDatosPrueba.CrearAsync();
        var hoy = new DateTime(2026, 11, 26, 12, 0, 0, DateTimeKind.Utc);
        await using (var db = bd.NuevoContexto())
        {
            db.Promociones.AddRange(
                new Promocion { Nombre = "Vigente", Porcentaje = 10, FechaInicio = hoy.AddDays(-1), FechaFin = hoy.AddDays(1), Activa = true },
                new Promocion { Nombre = "Vencida", Porcentaje = 50, FechaInicio = hoy.AddDays(-10), FechaFin = hoy.AddDays(-1), Activa = true },
                new Promocion { Nombre = "Futura", Porcentaje = 50, FechaInicio = hoy.AddDays(1), FechaFin = hoy.AddDays(5), Activa = true },
                new Promocion { Nombre = "Pausada", Porcentaje = 50, FechaInicio = hoy.AddDays(-1), FechaFin = hoy.AddDays(1), Activa = false });
            await db.SaveChangesAsync();
        }

        await using var ctx = bd.NuevoContexto();
        var vigentes = await new ServicioPrecios(ctx, new RelojFijo(hoy)).ObtenerPromocionesVigentesAsync();

        Assert.Equal(["Vigente"], vigentes.Select(v => v.Nombre).ToArray());
    }
}
