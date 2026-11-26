using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LlamitasStore.Models;

[Table("usuario")]
public class Usuario
{
    [Key]
    [Column("id_usuario")]
    public int Id { get; set; }

    [Column("nombre")]
    public string Nombre { get; set; } = string.Empty;

    [Column("apellido")]
    public string Apellido { get; set; } = string.Empty;

    [Column("correo")]
    public string Email { get; set; } = string.Empty;

    [Column("contrasena")]
    public string Contrasena { get; set; } = string.Empty;

    [Column("telefono")]
    public string? Telefono { get; set; }

    [Column("id_rol")]
    public int RolId { get; set; }

    [ForeignKey("RolId")]
    public Rol? Rol { get; set; }

    [Column("estado")]
    public string Estado { get; set; } = "Activo";
}