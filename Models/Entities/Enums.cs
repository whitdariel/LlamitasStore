namespace LlamitasStore.Models.Entities;

public enum EstadoUsuario { Activo = 1, Inactivo = 2, Suspendido = 3 }

public enum EstadoProducto { Activo = 1, Inactivo = 2, Descontinuado = 3 }

/// <summary>Tipo de componente para el PC Builder. "Ninguno" = periféricos y accesorios.</summary>
public enum TipoComponente
{
    Ninguno = 0,
    Procesador = 1,
    PlacaMadre = 2,
    MemoriaRam = 3,
    TarjetaGrafica = 4,
    Almacenamiento = 5,
    FuentePoder = 6,
    Gabinete = 7,
    Refrigeracion = 8
}

public enum TipoMovimiento { Entrada = 1, Salida = 2, Ajuste = 3, Devolucion = 4 }

public enum EstadoPedido { Pendiente = 1, Confirmado = 2, EnPreparacion = 3, Enviado = 4, Entregado = 5, Cancelado = 6 }

public enum MetodoPago { ContraEntrega = 1, Transferencia = 2 }

public enum EstadoPago { Pendiente = 1, Confirmado = 2, Rechazado = 3, Reembolsado = 4 }

public enum TipoDescuento { Porcentaje = 1, MontoFijo = 2 }

public enum EstadoOrdenCompra { Borrador = 1, Recibida = 2, Cancelada = 3 }
