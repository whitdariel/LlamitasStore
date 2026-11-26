using System.ComponentModel.DataAnnotations;
using LlamitasStore.Models.Entities;
using LlamitasStore.ViewModels;

namespace LlamitasStore.Areas.Admin.Models;

public class CategoriaFormVm
{
    public int? Id { get; set; }
    [Required(ErrorMessage = Mensajes.Requerido), StringLength(60, ErrorMessage = Mensajes.Largo)] [Display(Name = "Nombre")]
    public string Nombre { get; set; } = string.Empty;
    [StringLength(300, ErrorMessage = Mensajes.Largo)] [Display(Name = "Descripción")]
    public string? Descripcion { get; set; }
    [Display(Name = "Ícono")] public string Icono { get; set; } = "accesorio";
    [Range(0, 999)] [Display(Name = "Orden en menús")] public int Orden { get; set; }
    [Display(Name = "Tipo de componente (PC Builder)")] public TipoComponente TipoComponente { get; set; }
    [Display(Name = "Visible en la tienda")] public bool Activa { get; set; } = true;

    public static readonly string[] Iconos = ["cpu", "gpu", "placa", "ram", "ssd", "psu", "gabinete", "cooler", "monitor", "teclado", "raton", "auriculares", "accesorio"];
}

public class MarcaFormVm
{
    public int? Id { get; set; }
    [Required(ErrorMessage = Mensajes.Requerido), StringLength(60, ErrorMessage = Mensajes.Largo)] [Display(Name = "Nombre")]
    public string Nombre { get; set; } = string.Empty;
    [StringLength(300, ErrorMessage = Mensajes.Largo)] [Display(Name = "Descripción")]
    public string? Descripcion { get; set; }
    [StringLength(200, ErrorMessage = Mensajes.Largo), Url(ErrorMessage = "Escribe una URL completa (https://…).")] [Display(Name = "Sitio web")]
    public string? SitioWeb { get; set; }
    [Display(Name = "Destacada en el inicio")] public bool Destacada { get; set; }
    [Display(Name = "Activa")] public bool Activa { get; set; } = true;
}

public class ProveedorFormVm
{
    public int? Id { get; set; }
    [Required(ErrorMessage = Mensajes.Requerido), StringLength(100, ErrorMessage = Mensajes.Largo)] [Display(Name = "Nombre o razón social")]
    public string Nombre { get; set; } = string.Empty;
    [StringLength(30, ErrorMessage = Mensajes.Largo)] [Display(Name = "NIT")] public string? Nit { get; set; }
    [RegularExpression(Mensajes.PatronTelefono, ErrorMessage = Mensajes.Telefono)] [Display(Name = "Teléfono")] public string? Telefono { get; set; }
    [EmailAddress(ErrorMessage = Mensajes.Correo), StringLength(150)] [Display(Name = "Correo")] public string? Email { get; set; }
    [StringLength(200, ErrorMessage = Mensajes.Largo)] [Display(Name = "Dirección")] public string? Direccion { get; set; }
    [Display(Name = "Activo")] public bool Activo { get; set; } = true;
}

public class PromocionFormVm : IValidatableObject
{
    public int? Id { get; set; }
    [Required(ErrorMessage = Mensajes.Requerido), StringLength(100, ErrorMessage = Mensajes.Largo)] [Display(Name = "Nombre (lo ve el cliente)")]
    public string Nombre { get; set; } = string.Empty;
    [StringLength(300)] [Display(Name = "Descripción")] public string? Descripcion { get; set; }
    [Range(typeof(decimal), "1", "90", ErrorMessage = "Entre {1}% y {2}%.")] [Display(Name = "Descuento (%)")] public decimal Porcentaje { get; set; } = 10;
    [Display(Name = "Desde")] public DateTime FechaInicio { get; set; } = DateTime.UtcNow.Date;
    [Display(Name = "Hasta (sin incluir)")] public DateTime FechaFin { get; set; } = DateTime.UtcNow.Date.AddDays(7);
    [Display(Name = "Activa")] public bool Activa { get; set; } = true;
    [Display(Name = "Categoría")] public int? CategoriaId { get; set; }
    [Display(Name = "Marca")] public int? MarcaId { get; set; }
    [Display(Name = "Producto")] public int? ProductoId { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext _)
    {
        if (FechaFin <= FechaInicio) yield return new("La fecha final debe ser posterior a la inicial.", [nameof(FechaFin)]);
        var destinos = new[] { CategoriaId, MarcaId, ProductoId }.Count(x => x is > 0);
        if (destinos != 1) yield return new("Elige exactamente uno: una categoría, una marca o un producto.", [nameof(CategoriaId)]);
    }
}

public class CuponFormVm : IValidatableObject
{
    public int? Id { get; set; }
    [Required(ErrorMessage = Mensajes.Requerido), StringLength(40, MinimumLength = 3, ErrorMessage = "Entre {2} y {1} caracteres.")]
    [RegularExpression(@"^[A-Za-z0-9\-_]+$", ErrorMessage = "Usa solo letras, números, guiones.")] [Display(Name = "Código")]
    public string Codigo { get; set; } = string.Empty;
    [StringLength(200)] [Display(Name = "Descripción")] public string? Descripcion { get; set; }
    [Display(Name = "Tipo")] public TipoDescuento Tipo { get; set; } = TipoDescuento.Porcentaje;
    [Range(typeof(decimal), "0.01", "100000", ErrorMessage = "Debe ser mayor a 0.")] [Display(Name = "Valor (% o Bs.)")] public decimal Valor { get; set; } = 10;
    [Range(typeof(decimal), "0", "100000")] [Display(Name = "Compra mínima (Bs.)")] public decimal MontoMinimo { get; set; }
    [Display(Name = "Desde")] public DateTime FechaInicio { get; set; } = DateTime.UtcNow.Date;
    [Display(Name = "Hasta (sin incluir)")] public DateTime FechaFin { get; set; } = DateTime.UtcNow.Date.AddMonths(1);
    [Range(1, 1000000)] [Display(Name = "Usos totales (vacío = sin límite)")] public int? UsosMaximos { get; set; }
    [Range(1, 1000)] [Display(Name = "Usos por cliente (vacío = sin límite)")] public int? UsosPorUsuario { get; set; }
    [Display(Name = "Activo")] public bool Activo { get; set; } = true;

    public IEnumerable<ValidationResult> Validate(ValidationContext _)
    {
        if (FechaFin <= FechaInicio) yield return new("La fecha final debe ser posterior a la inicial.", [nameof(FechaFin)]);
        if (Tipo == TipoDescuento.Porcentaje && Valor > 90) yield return new("Un cupón porcentual no puede superar el 90%.", [nameof(Valor)]);
    }
}

public class ConfiguracionVm
{
    [Required(ErrorMessage = Mensajes.Requerido), StringLength(60)] [Display(Name = "Nombre de la tienda")]
    public string NombreTienda { get; set; } = "LlamitasStore";
    [Required(ErrorMessage = Mensajes.Requerido), StringLength(8)] [Display(Name = "Símbolo de moneda")]
    public string Moneda { get; set; } = "Bs.";
    [EmailAddress(ErrorMessage = Mensajes.Correo), StringLength(150)] [Display(Name = "Correo de contacto")]
    public string? Email { get; set; }
    [RegularExpression(Mensajes.PatronTelefono, ErrorMessage = Mensajes.Telefono)] [Display(Name = "Teléfono de contacto")]
    public string? Telefono { get; set; }
    [StringLength(200)] [Display(Name = "Dirección")] public string? Direccion { get; set; }
    [Range(typeof(decimal), "0", "10000", ErrorMessage = "Entre {1} y {2}.")] [Display(Name = "Costo de envío (Bs.)")]
    public decimal CostoEnvio { get; set; }
    [Range(typeof(decimal), "0", "1000000", ErrorMessage = "Entre {1} y {2}.")] [Display(Name = "Envío gratis desde (Bs., 0 = nunca)")]
    public decimal EnvioGratisDesde { get; set; }
    [Required(ErrorMessage = Mensajes.Requerido), StringLength(1000)] [Display(Name = "Instrucciones para transferencias")]
    public string InstruccionesTransferencia { get; set; } = string.Empty;
}

public record Pagina<T>(IReadOnlyList<T> Items, int Total, int Actual, int Tamano = 20)
{
    public int TotalPaginas => Math.Max(1, (int)Math.Ceiling(Total / (double)Tamano));
}
