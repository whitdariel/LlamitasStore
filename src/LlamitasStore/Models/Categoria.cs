using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LlamitasStore.Models;

[Table("categoria")]
public class Categoria
{
    [Key]
    [Column("id_categoria")]
    public int Id { get; set; }

    [Column("nombre")]
    public string NombreCategoria { get; set; } = string.Empty;

    [Column("descripcion")]
    public string? Descripcion { get; set; }
}