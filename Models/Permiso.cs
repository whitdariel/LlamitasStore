namespace LlamitasStore.Models
{
    public class Permiso
    {
        public int Id { get; set; }
        public string NombrePermiso { get; set; } = string.Empty; // Ejemplo: GestionarComponentes, VerVentas, etc.
        public string Descripcion { get; set; } = string.Empty;
        public int RolId { get; set; }
        public Rol? Rol { get; set; }
    }
}