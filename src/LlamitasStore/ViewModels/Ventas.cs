using System.ComponentModel.DataAnnotations;
using LlamitasStore.Models.Entities;
using LlamitasStore.Services.Catalogo;
using LlamitasStore.Services.Ventas;

namespace LlamitasStore.ViewModels;

public record ItemCarritoVista(
    int ProductoId, string Nombre, string Slug, string Sku, string Marca, string? ImagenUrl,
    PrecioCalculado Precio, int Cantidad, int Stock, bool Activo)
{
    public decimal Subtotal => Math.Round(Precio.PrecioFinal * Cantidad, 2, MidpointRounding.AwayFromZero);
    public bool SinStock => !Activo || Stock == 0;
    public bool CantidadExcedeStock => Activo && Stock > 0 && Cantidad > Stock;
    public bool Comprable => !SinStock && !CantidadExcedeStock;
    /// <summary>Máximo que se puede elegir en el selector de cantidad.</summary>
    public int MaximoSeleccionable => Math.Max(1, Math.Min(Stock, Services.Ventas.ServicioCarrito.MaximoPorProducto));
}

public record CarritoVista(
    IReadOnlyList<ItemCarritoVista> Items,
    Totales Totales,
    string? CodigoCupon,
    CuponAplicable? CuponAplicado,
    string? ErrorCupon)
{
    public static CarritoVista Vacio(decimal envioGratisDesde) => new([], new Totales(0, 0, 0, 0, envioGratisDesde), null, null, null);
    public int CantidadTotal => Items.Sum(i => i.Cantidad);
    public bool EstaVacio => Items.Count == 0;
    public bool PuedeComprar => !EstaVacio && Items.All(i => i.Comprable);
}

public enum EstadoAgregar { Agregado, Ajustado, SinStock, NoDisponible }

public record ResultadoCarrito(EstadoAgregar Estado, int CantidadEnCarrito, int TotalItems, string Mensaje);

public class CheckoutVm
{
    [Required(ErrorMessage = "Elige una dirección de envío.")]
    [Range(1, int.MaxValue, ErrorMessage = "Elige una dirección de envío.")]
    [Display(Name = "Dirección de envío")]
    public int? DireccionId { get; set; }

    [Required(ErrorMessage = "Elige un método de pago.")]
    [Display(Name = "Método de pago")]
    public MetodoPago? MetodoPago { get; set; }

    [StringLength(100, ErrorMessage = Mensajes.Largo)]
    [RegularExpression(@"^[A-Za-z0-9\-/ ]*$", ErrorMessage = "Usa solo letras, números, guiones y barras.")]
    [Display(Name = "N.º de comprobante (si ya transferiste)")]
    public string? ReferenciaPago { get; set; }

    [StringLength(500, ErrorMessage = Mensajes.Largo)]
    [Display(Name = "Notas para la entrega (opcional)")]
    public string? Notas { get; set; }
}

public record CheckoutPaginaVm(CarritoVista Carrito, IReadOnlyList<Direccion> Direcciones, CheckoutVm Formulario, string InstruccionesTransferencia);

public record ResultadoPedido(string? Numero, IReadOnlyList<string> Errores)
{
    public bool Exito => Numero is not null;
}

public static class TextosPedido
{
    public static string Estado(EstadoPedido e) => e switch
    {
        EstadoPedido.Pendiente => "Pendiente",
        EstadoPedido.Confirmado => "Confirmado",
        EstadoPedido.EnPreparacion => "En preparación",
        EstadoPedido.Enviado => "Enviado",
        EstadoPedido.Entregado => "Entregado",
        EstadoPedido.Cancelado => "Cancelado",
        _ => e.ToString()
    };

    public static string Metodo(MetodoPago m) => m switch
    {
        MetodoPago.ContraEntrega => "Pago contra entrega",
        MetodoPago.Transferencia => "Transferencia bancaria",
        _ => m.ToString()
    };

    public static string Pago(EstadoPago e) => e switch
    {
        EstadoPago.Pendiente => "Pendiente",
        EstadoPago.Confirmado => "Confirmado",
        EstadoPago.Rechazado => "Rechazado",
        EstadoPago.Reembolsado => "Reembolsado",
        _ => e.ToString()
    };

    /// <summary>Pasos que se muestran en la línea de tiempo (Cancelado se muestra aparte).</summary>
    public static readonly EstadoPedido[] Flujo =
        [EstadoPedido.Pendiente, EstadoPedido.Confirmado, EstadoPedido.EnPreparacion, EstadoPedido.Enviado, EstadoPedido.Entregado];
}
