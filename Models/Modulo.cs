using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LlamitasStore.Models;

[Table("modulo")]
public class Modulo
{
    [Key]
    [Column("id_modulo")]
    public int Id { get; set; }

    [Column("nombre")]
    [StringLength(50)]
    public string Nombre { get; set; } = string.Empty;

    [Column("descripcion")]
    [StringLength(150)]
    public string? Descripcion { get; set; }

    [Column("id_categoria")]
    public int CategoriaId { get; set; }

    [ForeignKey(nameof(CategoriaId))]
    public Categoria? Categoria { get; set; }
}