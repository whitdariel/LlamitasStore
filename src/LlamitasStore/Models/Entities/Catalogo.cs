namespace LlamitasStore.Models.Entities;

public class Categoria
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Descripcion { get; set; }
    /// <summary>Nombre del icono (archivo SVG en /images/categorias).</summary>
    public string? Icono { get; set; }
    public int Orden { get; set; }
    public bool Activa { get; set; } = true;
    public TipoComponente TipoComponente { get; set; } = TipoComponente.Ninguno;

    public ICollection<Producto> Productos { get; set; } = new List<Producto>();
}

public class Marca
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Descripcion { get; set; }
    public string? SitioWeb { get; set; }
    public bool Destacada { get; set; }
    public bool Activa { get; set; } = true;

    public ICollection<Producto> Productos { get; set; } = new List<Producto>();
}

public class Producto
{
    public int Id { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Modelo { get; set; }
    public string DescripcionCorta { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;

    public int CategoriaId { get; set; }
    public Categoria Categoria { get; set; } = null!;
    public int MarcaId { get; set; }
    public Marca Marca { get; set; } = null!;

    public decimal Precio { get; set; }
    /// <summary>Precio promocional opcional. Debe ser menor que <see cref="Precio"/>.</summary>
    public decimal? PrecioOferta { get; set; }
    public decimal PrecioCosto { get; set; }

    public int Stock { get; set; }
    public int StockMinimo { get; set; } = 3;
    public EstadoProducto Estado { get; set; } = EstadoProducto.Activo;
    public bool Destacado { get; set; }
    public bool EsNuevo { get; set; }
    public int GarantiaMeses { get; set; } = 12;

    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;
    public DateTime? FechaActualizacion { get; set; }

    // Valores desnormalizados que mantienen los servicios (reseñas y pedidos) para listar y ordenar rápido.
    public decimal ValoracionPromedio { get; set; }
    public int TotalResenas { get; set; }
    public int TotalVendidos { get; set; }

    public ICollection<ProductoImagen> Imagenes { get; set; } = new List<ProductoImagen>();
    public ICollection<ProductoEspecificacion> Especificaciones { get; set; } = new List<ProductoEspecificacion>();
    public ProductoCompatibilidad? Compatibilidad { get; set; }
    public ICollection<Resena> Resenas { get; set; } = new List<Resena>();

    public bool Disponible => Estado == EstadoProducto.Activo && Stock > 0;
    public bool StockBajo => Estado == EstadoProducto.Activo && Stock <= StockMinimo;
}

public class ProductoImagen
{
    public int Id { get; set; }
    public int ProductoId { get; set; }
    public Producto Producto { get; set; } = null!;
    public string Url { get; set; } = string.Empty;
    public string TextoAlternativo { get; set; } = string.Empty;
    public int Orden { get; set; }
    public bool EsPrincipal { get; set; }
}

/// <summary>Especificación técnica mostrada en la ficha (texto libre agrupado).</summary>
public class ProductoEspecificacion
{
    public int Id { get; set; }
    public int ProductoId { get; set; }
    public Producto Producto { get; set; } = null!;
    public string Grupo { get; set; } = "General";
    public string Nombre { get; set; } = string.Empty;
    public string Valor { get; set; } = string.Empty;
    public int Orden { get; set; }
}

/// <summary>
/// Atributos técnicos tipados que usa el PC Builder para comprobar compatibilidad.
/// Solo se rellenan los campos que aplican al tipo de componente.
/// Listas (tipos de memoria, factores de forma, sockets) se guardan separadas por comas.
/// </summary>
public class ProductoCompatibilidad
{
    public int ProductoId { get; set; }
    public Producto Producto { get; set; } = null!;

    /// <summary>CPU y placa: socket (AM4, AM5, LGA1700…).</summary>
    public string? Socket { get; set; }
    /// <summary>CPU: tipos soportados ("DDR4,DDR5"). Placa y RAM: un único tipo.</summary>
    public string? TiposMemoria { get; set; }
    /// <summary>Placa: factor de forma (ATX, Micro-ATX, Mini-ITX).</summary>
    public string? FactorForma { get; set; }
    /// <summary>Gabinete: factores de forma de placa admitidos.</summary>
    public string? FactoresFormaSoportados { get; set; }
    /// <summary>Placa: ranuras de memoria.</summary>
    public int? SlotsMemoria { get; set; }
    /// <summary>Placa: memoria máxima admitida en GB.</summary>
    public int? MemoriaMaximaGb { get; set; }
    /// <summary>RAM: capacidad total del kit en GB.</summary>
    public int? CapacidadGb { get; set; }
    /// <summary>RAM: número de módulos del kit.</summary>
    public int? Modulos { get; set; }
    public int? VelocidadMhz { get; set; }
    /// <summary>CPU (TDP) y GPU (potencia de placa): consumo en W.</summary>
    public int? ConsumoW { get; set; }
    /// <summary>Fuente: potencia nominal en W.</summary>
    public int? PotenciaW { get; set; }
    /// <summary>GPU: fuente recomendada por el fabricante en W.</summary>
    public int? FuenteRecomendadaW { get; set; }
    /// <summary>GPU: largo en mm.</summary>
    public int? LargoMm { get; set; }
    /// <summary>Gabinete: largo máximo de GPU en mm.</summary>
    public int? LargoGpuMaximoMm { get; set; }
    /// <summary>Disipador: altura en mm.</summary>
    public int? AlturaMm { get; set; }
    /// <summary>Gabinete: altura máxima del disipador en mm.</summary>
    public int? AlturaCoolerMaximaMm { get; set; }
    /// <summary>Disipador: sockets compatibles.</summary>
    public string? SocketsSoportados { get; set; }
    /// <summary>Almacenamiento: "M.2" o "SATA".</summary>
    public string? Interfaz { get; set; }
    /// <summary>Placa: ranuras M.2.</summary>
    public int? SlotsM2 { get; set; }
    /// <summary>Placa: puertos SATA.</summary>
    public int? PuertosSata { get; set; }
    /// <summary>CPU: tiene gráficos integrados.</summary>
    public bool? GraficosIntegrados { get; set; }
    /// <summary>CPU: incluye disipador en la caja.</summary>
    public bool? IncluyeCooler { get; set; }
}
