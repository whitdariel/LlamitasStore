using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LlamitasStore.Models;

[Table("rol")]
public class Rol
{
    [Key]
    [Column("id_rol")]
    public int Id { get; set; }

    [Column("nombre")]
    public string NombreRol { get; set; } = string.Empty;

    [Column("descripcion")]
    public string? Descripcion { get; set; }
}