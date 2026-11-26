namespace LlamitasStore.Models.Entities;

public class Direccion
{
    public int Id { get; set; }
    public int UsuarioId { get; set; }
    public Usuario Usuario { get; set; } = null!;
    public string Alias { get; set; } = "Casa";
    public string Destinatario { get; set; } = string.Empty;
    public string Telefono { get; set; } = string.Empty;
    public string Departamento { get; set; } = string.Empty;
    public string Ciudad { get; set; } = string.Empty;
    public string DireccionLinea { get; set; } = string.Empty;
    public string? Referencia { get; set; }
    public bool EsPredeterminada { get; set; }
}

public class Favorito
{
    public int UsuarioId { get; set; }
    public Usuario Usuario { get; set; } = null!;
    public int ProductoId { get; set; }
    public Producto Producto { get; set; } = null!;
    public DateTime Fecha { get; set; } = DateTime.UtcNow;
}

public class Resena
{
    public int Id { get; set; }
    public int ProductoId { get; set; }
    public Producto Producto { get; set; } = null!;
    public int UsuarioId { get; set; }
    public Usuario Usuario { get; set; } = null!;
    /// <summary>1 a 5 estrellas.</summary>
    public int Calificacion { get; set; }
    public string Titulo { get; set; } = string.Empty;
    public string Comentario { get; set; } = string.Empty;
    public DateTime Fecha { get; set; } = DateTime.UtcNow;
    public bool Aprobada { get; set; } = true;
    /// <summary>El autor compró y recibió el producto.</summary>
    public bool CompraVerificada { get; set; }
}

public class ConfiguracionPc
{
    public int Id { get; set; }
    public int UsuarioId { get; set; }
    public Usuario Usuario { get; set; } = null!;
    public string Nombre { get; set; } = string.Empty;
    public string? Notas { get; set; }
    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;
    public DateTime? FechaActualizacion { get; set; }

    public ICollection<ConfiguracionPcItem> Items { get; set; } = new List<ConfiguracionPcItem>();
}

public class ConfiguracionPcItem
{
    public int Id { get; set; }
    public int ConfiguracionPcId { get; set; }
    public ConfiguracionPc ConfiguracionPc { get; set; } = null!;
    public TipoComponente Tipo { get; set; }
    public int ProductoId { get; set; }
    public Producto Producto { get; set; } = null!;
    public int Cantidad { get; set; } = 1;
}

public class Notificacion
{
    public int Id { get; set; }
    public int UsuarioId { get; set; }
    public Usuario Usuario { get; set; } = null!;
    public string Titulo { get; set; } = string.Empty;
    public string Mensaje { get; set; } = string.Empty;
    public string? Url { get; set; }
    public bool Leida { get; set; }
    public DateTime Fecha { get; set; } = DateTime.UtcNow;
}
