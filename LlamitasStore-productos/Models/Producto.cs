using System.ComponentModel.DataAnnotations;

namespace LlamitasStore.Models;

public class Producto
{
    public int IdProducto { get; set; }

    [Display(Name = "Categoría")]
    [Range(1, int.MaxValue, ErrorMessage = "Seleccione una categoría.")]
    public int IdCategoria { get; set; }

    // Solo lectura: viene del JOIN con Categoria para mostrarlo en la lista.
    // Es nullable a propósito: si no, MVC lo exigiría como campo obligatorio del formulario.
    public string? CategoriaNombre { get; set; }

    [Required(ErrorMessage = "El nombre es obligatorio.")]
    [StringLength(100)]
    public string Nombre { get; set; } = string.Empty;

    [StringLength(50)]
    public string? Marca { get; set; }

    [StringLength(50)]
    public string? Modelo { get; set; }

    [StringLength(200)]
    [Display(Name = "Descripción")]
    public string? Descripcion { get; set; }

    [Display(Name = "Precio de compra (Bs)")]
    [Range(0, 99999999.99, ErrorMessage = "El precio de compra no puede ser negativo.")]
    public decimal PrecioCompra { get; set; }

    [Display(Name = "Precio de venta (Bs)")]
    [Range(0, 99999999.99, ErrorMessage = "El precio de venta no puede ser negativo.")]
    public decimal PrecioVenta { get; set; }

    // El stock lo mantienen los triggers (Inventario suma, Detalle_Venta resta);
    // por eso no se edita desde el formulario.
    public int StockActual { get; set; }

    [Required]
    public string Estado { get; set; } = "Activo";

    public decimal Margen => PrecioVenta - PrecioCompra;
}
