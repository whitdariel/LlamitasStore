using System.Collections.Generic;

namespace LlamitasStore.Models
{
    public class Categoria
    {
        public int Id { get; set; }
        public string NombreCategoria { get; set; } = string.Empty; // Ej: Tarjetas Gráficas, Procesadores, Placas Madre
        public string Descripcion { get; set; } = string.Empty;
    }
}