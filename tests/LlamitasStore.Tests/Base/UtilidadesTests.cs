using LlamitasStore.Data;
using LlamitasStore.Utilidades;

namespace LlamitasStore.Tests.Base;

public class UtilidadesTests
{
    [Theory]
    [InlineData("Tarjetas gráficas", "tarjetas-graficas")]
    [InlineData("AMD Ryzen 7 7800X3D", "amd-ryzen-7-7800x3d")]
    [InlineData("Crucial MX500 1TB SATA 2.5\"", "crucial-mx500-1tb-sata-2-5")]
    [InlineData("  --Ñandú   Cooler!!  ", "nandu-cooler")]
    [InlineData("", "")]
    public void Slug_normaliza_acentos_espacios_y_simbolos(string entrada, string esperado) =>
        Assert.Equal(esperado, Slugs.Generar(entrada));

    [Fact]
    public void Slug_respeta_el_largo_maximo_sin_guion_final()
    {
        var slug = Slugs.Generar("abc def ghi", maximo: 8);
        Assert.Equal("abc-def", slug);
    }

    [Theory]
    [InlineData("Id", "id")]
    [InlineData("ProductoId", "producto_id")]
    [InlineData("PrecioOferta", "precio_oferta")]
    [InlineData("NormalizedUserName", "normalized_user_name")]
    [InlineData("TwoFactorEnabled", "two_factor_enabled")]
    public void Columnas_se_nombran_en_snake_case(string propiedad, string esperado) =>
        Assert.Equal(esperado, AppDbContext.ASnakeCase(propiedad));
}
