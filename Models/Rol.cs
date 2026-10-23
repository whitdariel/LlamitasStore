using System.Collections.Generic;

namespace LlamitasStore.Models
{
    public class Rol
    {
        public int Id { get; set; }
        public string NombreRol { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;
        public ICollection<Permiso> Permisos { get; set; } = new List<Permiso>();
    }
}