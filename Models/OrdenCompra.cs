using System;
using System.Collections.Generic;

namespace LlamitasStore.Models
{
    public class OrdenCompra
    {
        public int Id { get; set; }
        public DateTime Fecha { get; set; } = DateTime.Now;
        public string Proveedor { get; set; } = string.Empty;
        public decimal Total { get; set; }
        public ICollection<DetalleOrdenCompra> Detalles { get; set; } = new List<DetalleOrdenCompra>();
    }
}