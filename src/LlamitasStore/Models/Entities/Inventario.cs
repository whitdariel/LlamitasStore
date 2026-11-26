namespace LlamitasStore.Models.Entities;

public class Proveedor
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string? Nit { get; set; }
    public string? Telefono { get; set; }
    public string? Email { get; set; }
    public string? Direccion { get; set; }
    public bool Activo { get; set; } = true;

    public ICollection<OrdenCompra> OrdenesCompra { get; set; } = new List<OrdenCompra>();
}

/// <summary>Compra de mercadería a un proveedor (heredado del proyecto original). Al recibirse, suma stock.</summary>
public class OrdenCompra
{
    public int Id { get; set; }
    public string Numero { get; set; } = string.Empty;
    public int ProveedorId { get; set; }
    public Proveedor Proveedor { get; set; } = null!;
    public DateTime Fecha { get; set; } = DateTime.UtcNow;
    public EstadoOrdenCompra Estado { get; set; } = EstadoOrdenCompra.Borrador;
    public decimal Total { get; set; }
    public string? Notas { get; set; }
    public int? UsuarioId { get; set; }
    public Usuario? Usuario { get; set; }
    public DateTime? FechaRecepcion { get; set; }

    public ICollection<DetalleOrdenCompra> Detalles { get; set; } = new List<DetalleOrdenCompra>();
}

public class DetalleOrdenCompra
{
    public int Id { get; set; }
    public int OrdenCompraId { get; set; }
    public OrdenCompra OrdenCompra { get; set; } = null!;
    public int ProductoId { get; set; }
    public Producto Producto { get; set; } = null!;
    public int Cantidad { get; set; }
    public decimal CostoUnitario { get; set; }
}

/// <summary>Registro de cada cambio de stock. Producto.Stock es la única fuente de verdad.</summary>
public class MovimientoInventario
{
    public int Id { get; set; }
    public int ProductoId { get; set; }
    public Producto Producto { get; set; } = null!;
    public TipoMovimiento Tipo { get; set; }
    /// <summary>Positivo suma stock, negativo lo resta.</summary>
    public int Cantidad { get; set; }
    public int StockResultante { get; set; }
    public string Motivo { get; set; } = string.Empty;
    public int? PedidoId { get; set; }
    public Pedido? Pedido { get; set; }
    public int? OrdenCompraId { get; set; }
    public OrdenCompra? OrdenCompra { get; set; }
    public int? UsuarioId { get; set; }
    public Usuario? Usuario { get; set; }
    public DateTime Fecha { get; set; } = DateTime.UtcNow;
}

public class HistorialPrecio
{
    public int Id { get; set; }
    public int ProductoId { get; set; }
    public Producto Producto { get; set; } = null!;
    public decimal PrecioAnterior { get; set; }
    public decimal PrecioNuevo { get; set; }
    public decimal? PrecioOfertaAnterior { get; set; }
    public decimal? PrecioOfertaNuevo { get; set; }
    public int? UsuarioId { get; set; }
    public Usuario? Usuario { get; set; }
    public DateTime Fecha { get; set; } = DateTime.UtcNow;
}
