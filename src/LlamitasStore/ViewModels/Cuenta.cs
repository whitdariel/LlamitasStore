using System.ComponentModel.DataAnnotations;
using LlamitasStore.Models.Entities;

namespace LlamitasStore.ViewModels;

public static class Mensajes
{
    public const string Requerido = "Este campo es obligatorio.";
    public const string Correo = "Escribe un correo válido.";
    public const string Largo = "Máximo {1} caracteres.";
    public const string Telefono = "Escribe un teléfono válido (7 a 15 dígitos, puede empezar con +).";
    public const string PatronTelefono = @"^\+?[0-9 ]{7,18}$";
}

public class LoginVm
{
    [Required(ErrorMessage = Mensajes.Requerido), EmailAddress(ErrorMessage = Mensajes.Correo), StringLength(256, ErrorMessage = Mensajes.Largo)]
    [Display(Name = "Correo")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = Mensajes.Requerido), DataType(DataType.Password), StringLength(128, ErrorMessage = Mensajes.Largo)]
    [Display(Name = "Contraseña")]
    public string Password { get; set; } = string.Empty;

    [Display(Name = "Mantener la sesión iniciada")]
    public bool Recordarme { get; set; }

    public string? ReturnUrl { get; set; }
}

public class RegistroVm
{
    [Required(ErrorMessage = Mensajes.Requerido), StringLength(60, ErrorMessage = Mensajes.Largo)]
    [Display(Name = "Nombre")]
    public string Nombre { get; set; } = string.Empty;

    [Required(ErrorMessage = Mensajes.Requerido), StringLength(60, ErrorMessage = Mensajes.Largo)]
    [Display(Name = "Apellido")]
    public string Apellido { get; set; } = string.Empty;

    [Required(ErrorMessage = Mensajes.Requerido), EmailAddress(ErrorMessage = Mensajes.Correo), StringLength(256, ErrorMessage = Mensajes.Largo)]
    [Display(Name = "Correo")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = Mensajes.Requerido), DataType(DataType.Password)]
    [StringLength(128, MinimumLength = 8, ErrorMessage = "La contraseña debe tener entre {2} y {1} caracteres.")]
    [Display(Name = "Contraseña")]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = Mensajes.Requerido), DataType(DataType.Password)]
    [Compare(nameof(Password), ErrorMessage = "Las contraseñas no coinciden.")]
    [Display(Name = "Repite la contraseña")]
    public string ConfirmarPassword { get; set; } = string.Empty;

    [Range(typeof(bool), "true", "true", ErrorMessage = "Debes aceptar la política de privacidad.")]
    [Display(Name = "Acepto la política de privacidad")]
    public bool AceptaPrivacidad { get; set; }

    public string? ReturnUrl { get; set; }
}

public class OlvideContrasenaVm
{
    [Required(ErrorMessage = Mensajes.Requerido), EmailAddress(ErrorMessage = Mensajes.Correo), StringLength(256, ErrorMessage = Mensajes.Largo)]
    [Display(Name = "Correo de tu cuenta")]
    public string Email { get; set; } = string.Empty;
}

public class RestablecerContrasenaVm
{
    [Required(ErrorMessage = Mensajes.Requerido), EmailAddress(ErrorMessage = Mensajes.Correo)]
    [Display(Name = "Correo")]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string Token { get; set; } = string.Empty;

    [Required(ErrorMessage = Mensajes.Requerido), DataType(DataType.Password)]
    [StringLength(128, MinimumLength = 8, ErrorMessage = "La contraseña debe tener entre {2} y {1} caracteres.")]
    [Display(Name = "Nueva contraseña")]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = Mensajes.Requerido), DataType(DataType.Password)]
    [Compare(nameof(Password), ErrorMessage = "Las contraseñas no coinciden.")]
    [Display(Name = "Repite la nueva contraseña")]
    public string ConfirmarPassword { get; set; } = string.Empty;
}

public class PerfilVm
{
    [Required(ErrorMessage = Mensajes.Requerido), StringLength(60, ErrorMessage = Mensajes.Largo)]
    [Display(Name = "Nombre")]
    public string Nombre { get; set; } = string.Empty;

    [Required(ErrorMessage = Mensajes.Requerido), StringLength(60, ErrorMessage = Mensajes.Largo)]
    [Display(Name = "Apellido")]
    public string Apellido { get; set; } = string.Empty;

    [RegularExpression(Mensajes.PatronTelefono, ErrorMessage = Mensajes.Telefono)]
    [Display(Name = "Teléfono (opcional)")]
    public string? Telefono { get; set; }

    // Solo lectura en la vista.
    public string Email { get; set; } = string.Empty;
}

public class CambiarContrasenaVm
{
    /// <summary>Vacío para cuentas creadas con Google que todavía no tienen contraseña.</summary>
    [DataType(DataType.Password), StringLength(128, ErrorMessage = Mensajes.Largo)]
    [Display(Name = "Contraseña actual")]
    public string? Actual { get; set; }

    [Required(ErrorMessage = Mensajes.Requerido), DataType(DataType.Password)]
    [StringLength(128, MinimumLength = 8, ErrorMessage = "La contraseña debe tener entre {2} y {1} caracteres.")]
    [Display(Name = "Nueva contraseña")]
    public string Nueva { get; set; } = string.Empty;

    [Required(ErrorMessage = Mensajes.Requerido), DataType(DataType.Password)]
    [Compare(nameof(Nueva), ErrorMessage = "Las contraseñas no coinciden.")]
    [Display(Name = "Repite la nueva contraseña")]
    public string Confirmar { get; set; } = string.Empty;

    public bool TienePassword { get; set; }
    public IReadOnlyList<string> CuentasExternas { get; set; } = [];
}

public class DireccionVm
{
    public int? Id { get; set; }

    [Required(ErrorMessage = Mensajes.Requerido), StringLength(40, ErrorMessage = Mensajes.Largo)]
    [Display(Name = "Nombre de la dirección (ej. Casa, Oficina)")]
    public string Alias { get; set; } = "Casa";

    [Required(ErrorMessage = Mensajes.Requerido), StringLength(120, ErrorMessage = Mensajes.Largo)]
    [Display(Name = "Quién recibe")]
    public string Destinatario { get; set; } = string.Empty;

    [Required(ErrorMessage = Mensajes.Requerido), RegularExpression(Mensajes.PatronTelefono, ErrorMessage = Mensajes.Telefono)]
    [Display(Name = "Teléfono de contacto")]
    public string Telefono { get; set; } = string.Empty;

    [Required(ErrorMessage = Mensajes.Requerido)]
    [Display(Name = "Departamento")]
    public string Departamento { get; set; } = "La Paz";

    [Required(ErrorMessage = Mensajes.Requerido), StringLength(80, ErrorMessage = Mensajes.Largo)]
    [Display(Name = "Ciudad o municipio")]
    public string Ciudad { get; set; } = string.Empty;

    [Required(ErrorMessage = Mensajes.Requerido), StringLength(250, ErrorMessage = Mensajes.Largo)]
    [Display(Name = "Dirección (zona, calle y número)")]
    public string DireccionLinea { get; set; } = string.Empty;

    [StringLength(250, ErrorMessage = Mensajes.Largo)]
    [Display(Name = "Referencia (opcional)")]
    public string? Referencia { get; set; }

    [Display(Name = "Usar como dirección predeterminada")]
    public bool EsPredeterminada { get; set; }

    public static DireccionVm Desde(Direccion d) => new()
    {
        Id = d.Id, Alias = d.Alias, Destinatario = d.Destinatario, Telefono = d.Telefono, Departamento = d.Departamento,
        Ciudad = d.Ciudad, DireccionLinea = d.DireccionLinea, Referencia = d.Referencia, EsPredeterminada = d.EsPredeterminada
    };
}

public record ResumenCuentaVm(Usuario Usuario, IReadOnlyList<string> Roles, int Favoritos, int Direcciones, Direccion? Predeterminada);
