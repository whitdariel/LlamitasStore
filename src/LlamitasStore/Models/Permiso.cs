using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LlamitasStore.Models;

[Table("permiso")]
public class Permiso
{
    [Key]
    [Column("id_permiso")]
    public int Id { get; set; }

    [Column("id_rol")]
    public int IdRol { get; set; }

    [Column("id_modulo")]
    public int IdModulo { get; set; }

    [Column("puede_leer")]
    public bool PuedeLeer { get; set; }

    [Column("puede_crear")]
    public bool PuedeCrear { get; set; }

    [Column("puede_editar")]
    public bool PuedeEditar { get; set; }

    [Column("puede_eliminar")]
    public bool PuedeEliminar { get; set; }
}