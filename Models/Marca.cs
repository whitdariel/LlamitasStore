using System.Collections.Generic;

namespace LlamitasStore.Models
{
    public class Marca
    {
        public int Id { get; set; }
        public string NombreMarca { get; set; } = string.Empty; // Ej: ASUS, MSI, Corsair, Kingston
        public string Descripcion { get; set; } = string.Empty;
    }
}