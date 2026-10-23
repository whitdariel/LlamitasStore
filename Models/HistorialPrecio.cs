namespace LlamitasStore.Models;

public class HistorialPrecio
{
    public int Id { get; set; }
    public string Producto { get; set; } = string.Empty;
    public decimal PrecioAnterior { get; set; }
    public decimal PrecioNuevo { get; set; }
    public DateTime FechaCambio { get; set; }
}