namespace LlamitasStore.Models;

public class MovimientoInventario
{
    public int Id { get; set; }
    public string Producto { get; set; } = string.Empty;
    public string TipoMovimiento { get; set; } = string.Empty; // "Entrada" o "Salida"
    public int Cantidad { get; set; }
    public DateTime Fecha { get; set; }
    public string Motivo { get; set; } = string.Empty;
}