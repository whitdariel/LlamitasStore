namespace LlamitasStore.Models
{
    public class DetalleOrdenCompra
    {
        public int Id { get; set; }
        public int OrdenCompraId { get; set; }
        public OrdenCompra? OrdenCompra { get; set; }
        public string Componente { get; set; } = string.Empty; // Nombre o referencia del componente de PC comprado
        public int Cantidad { get; set; }
        public decimal PrecioUnitario { get; set; }
        public decimal Subtotal => Cantidad * PrecioUnitario;
    }
}