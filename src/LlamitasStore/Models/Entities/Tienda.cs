namespace LlamitasStore.Models.Entities;

public class MensajeContacto
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Asunto { get; set; } = string.Empty;
    public string Mensaje { get; set; } = string.Empty;
    public DateTime Fecha { get; set; } = DateTime.UtcNow;
    public bool Atendido { get; set; }
}

/// <summary>Ajuste general editable desde el panel (clave/valor).</summary>
public class AjusteTienda
{
    public string Clave { get; set; } = string.Empty;
    public string Valor { get; set; } = string.Empty;
    public string? Descripcion { get; set; }
}

public static class ClavesAjuste
{
    public const string NombreTienda = "tienda.nombre";
    public const string SimboloMoneda = "tienda.moneda";
    public const string EmailContacto = "tienda.email";
    public const string TelefonoContacto = "tienda.telefono";
    public const string DireccionTienda = "tienda.direccion";
    public const string CostoEnvio = "envio.costo";
    public const string EnvioGratisDesde = "envio.gratis_desde";
    public const string DatosTransferencia = "pago.transferencia_instrucciones";
}
