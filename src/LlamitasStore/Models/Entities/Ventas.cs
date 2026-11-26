namespace LlamitasStore.Models.Entities;

/// <summary>Carrito persistente: de un usuario registrado o de un visitante identificado por cookie.</summary>
public class Carrito
{
    public int Id { get; set; }
    public int? UsuarioId { get; set; }
    public Usuario? Usuario { get; set; }
    /// <summary>GUID de la cookie del visitante anónimo.</summary>
    public string? TokenAnonimo { get; set; }
    /// <summary>Cupón aplicado (se vuelve a validar al mostrar el carrito y al confirmar el pedido).</summary>
    public string? CodigoCupon { get; set; }
    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;
    public DateTime FechaActualizacion { get; set; } = DateTime.UtcNow;

    public ICollection<CarritoItem> Items { get; set; } = new List<CarritoItem>();
}

public class CarritoItem
{
    public int Id { get; set; }
    public int CarritoId { get; set; }
    public Carrito Carrito { get; set; } = null!;
    public int ProductoId { get; set; }
    public Producto Producto { get; set; } = null!;
    public int Cantidad { get; set; }
    public DateTime FechaAgregado { get; set; } = DateTime.UtcNow;
}

public class Pedido
{
    public int Id { get; set; }
    /// <summary>Número visible para el cliente, p. ej. LS-20261126-00042.</summary>
    public string Numero { get; set; } = string.Empty;
    public int UsuarioId { get; set; }
    public Usuario Usuario { get; set; } = null!;
    public DateTime Fecha { get; set; } = DateTime.UtcNow;
    public EstadoPedido Estado { get; set; } = EstadoPedido.Pendiente;

    public decimal Subtotal { get; set; }
    public decimal Descuento { get; set; }
    public decimal CostoEnvio { get; set; }
    public decimal Total { get; set; }

    public int? CuponId { get; set; }
    public Cupon? Cupon { get; set; }
    public string? CodigoCupon { get; set; }

    public MetodoPago MetodoPago { get; set; }
    public string? Notas { get; set; }

    // Copia de la dirección al momento de comprar: editar la libreta no altera pedidos pasados.
    public string EnvioDestinatario { get; set; } = string.Empty;
    public string EnvioTelefono { get; set; } = string.Empty;
    public string EnvioDepartamento { get; set; } = string.Empty;
    public string EnvioCiudad { get; set; } = string.Empty;
    public string EnvioDireccion { get; set; } = string.Empty;
    public string? EnvioReferencia { get; set; }

    public DateTime? FechaActualizacion { get; set; }

    public ICollection<DetallePedido> Detalles { get; set; } = new List<DetallePedido>();
    public ICollection<PedidoEstadoHistorial> Historial { get; set; } = new List<PedidoEstadoHistorial>();
    public ICollection<Pago> Pagos { get; set; } = new List<Pago>();
}

public class DetallePedido
{
    public int Id { get; set; }
    public int PedidoId { get; set; }
    public Pedido Pedido { get; set; } = null!;
    public int ProductoId { get; set; }
    public Producto Producto { get; set; } = null!;
    // Copia del producto al momento de comprar.
    public string NombreProducto { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public decimal PrecioUnitario { get; set; }
    public int Cantidad { get; set; }
    public decimal Subtotal { get; set; }
}

public class PedidoEstadoHistorial
{
    public int Id { get; set; }
    public int PedidoId { get; set; }
    public Pedido Pedido { get; set; } = null!;
    public EstadoPedido Estado { get; set; }
    public string? Comentario { get; set; }
    public int? UsuarioId { get; set; }
    public Usuario? Usuario { get; set; }
    public DateTime Fecha { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Pago de un pedido. Solo guarda una referencia (p. ej. número de comprobante de transferencia):
/// nunca números de tarjeta ni datos bancarios.
/// </summary>
public class Pago
{
    public int Id { get; set; }
    public int PedidoId { get; set; }
    public Pedido Pedido { get; set; } = null!;
    public MetodoPago Metodo { get; set; }
    public EstadoPago Estado { get; set; } = EstadoPago.Pendiente;
    public decimal Monto { get; set; }
    public string? Referencia { get; set; }
    public DateTime Fecha { get; set; } = DateTime.UtcNow;
    public DateTime? FechaConfirmacion { get; set; }
    public int? ConfirmadoPorId { get; set; }
    public Usuario? ConfirmadoPor { get; set; }
}

public class Cupon
{
    public int Id { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string? Descripcion { get; set; }
    public TipoDescuento Tipo { get; set; }
    public decimal Valor { get; set; }
    public decimal MontoMinimo { get; set; }
    public DateTime FechaInicio { get; set; }
    public DateTime FechaFin { get; set; }
    public int? UsosMaximos { get; set; }
    public int? UsosPorUsuario { get; set; }
    public int UsosActuales { get; set; }
    public bool Activo { get; set; } = true;
}

/// <summary>Descuento porcentual temporal sobre un producto, una marca o una categoría.</summary>
public class Promocion
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string? Descripcion { get; set; }
    public decimal Porcentaje { get; set; }
    public DateTime FechaInicio { get; set; }
    public DateTime FechaFin { get; set; }
    public bool Activa { get; set; } = true;
    public int? CategoriaId { get; set; }
    public Categoria? Categoria { get; set; }
    public int? MarcaId { get; set; }
    public Marca? Marca { get; set; }
    public int? ProductoId { get; set; }
    public Producto? Producto { get; set; }
}
