namespace LlamitasStore.ViewModels;

public record CategoriaResumen(string Nombre, string Slug, string? Icono, int TotalProductos);

public record InicioViewModel(IReadOnlyList<CategoriaResumen> Categorias);
