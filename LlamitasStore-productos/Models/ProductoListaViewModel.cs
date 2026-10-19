namespace LlamitasStore.Models;

public class ProductoListaViewModel
{
    public List<Producto> Productos { get; set; } = new();
    public List<Categoria> Categorias { get; set; } = new();
    public string? Buscar { get; set; }
    public int? IdCategoria { get; set; }
}
