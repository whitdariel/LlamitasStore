namespace LlamitasStore.ViewModels;

public record CategoriaResumen(string Nombre, string Slug, string? Icono, int TotalProductos)
{
    /// <summary>Id del símbolo en /images/iconos.svg (p. ej. "cat-cpu").</summary>
    public string IconoSprite => $"cat-{Icono ?? "accesorio"}";
}

public record MarcaResumen(string Nombre, string Slug, int TotalProductos);

public record InicioViewModel(
    IReadOnlyList<CategoriaResumen> Categorias,
    IReadOnlyList<TarjetaProducto> Ofertas,
    IReadOnlyList<TarjetaProducto> Destacados,
    IReadOnlyList<TarjetaProducto> Novedades,
    IReadOnlyList<TarjetaProducto> MasVendidos,
    IReadOnlyList<MarcaResumen> Marcas,
    int TotalProductos,
    int TotalMarcas);
