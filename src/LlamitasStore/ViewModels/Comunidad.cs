using System.ComponentModel.DataAnnotations;
using LlamitasStore.Services.Comunidad;

namespace LlamitasStore.ViewModels;

public class ResenaVm
{
    [Range(1, 5, ErrorMessage = "Elige de 1 a 5 estrellas.")]
    [Display(Name = "Calificación")]
    public int Calificacion { get; set; }

    [Required(ErrorMessage = Mensajes.Requerido)]
    [StringLength(ServicioResenas.TituloMax, MinimumLength = ServicioResenas.TituloMin, ErrorMessage = "Entre {2} y {1} caracteres.")]
    [Display(Name = "Título")]
    public string Titulo { get; set; } = string.Empty;

    [Required(ErrorMessage = Mensajes.Requerido)]
    [StringLength(ServicioResenas.ComentarioMax, MinimumLength = ServicioResenas.ComentarioMin, ErrorMessage = "Entre {2} y {1} caracteres.")]
    [Display(Name = "Tu opinión")]
    public string Comentario { get; set; } = string.Empty;
}

public class ContactoVm
{
    public static readonly string[] Asuntos =
        ["Consulta sobre un producto", "Compatibilidad de componentes", "Estado de mi pedido", "Garantía o devolución", "Otro"];

    [Required(ErrorMessage = Mensajes.Requerido), StringLength(100, MinimumLength = 2, ErrorMessage = "Entre {2} y {1} caracteres.")]
    [Display(Name = "Nombre")]
    public string Nombre { get; set; } = string.Empty;

    [Required(ErrorMessage = Mensajes.Requerido), EmailAddress(ErrorMessage = Mensajes.Correo), StringLength(150, ErrorMessage = Mensajes.Largo)]
    [Display(Name = "Correo")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = Mensajes.Requerido), StringLength(150, ErrorMessage = Mensajes.Largo)]
    [Display(Name = "Asunto")]
    public string Asunto { get; set; } = string.Empty;

    [StringLength(30, ErrorMessage = Mensajes.Largo)]
    [Display(Name = "Número de pedido (opcional)")]
    public string? NumeroPedido { get; set; }

    [Required(ErrorMessage = Mensajes.Requerido), StringLength(3000, MinimumLength = 10, ErrorMessage = "Entre {2} y {1} caracteres.")]
    [Display(Name = "Mensaje")]
    public string Mensaje { get; set; } = string.Empty;

    /// <summary>Trampa para bots: campo oculto que una persona deja vacío.</summary>
    public string? SitioWeb { get; set; }
}
