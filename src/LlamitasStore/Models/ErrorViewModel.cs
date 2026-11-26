namespace LlamitasStore.Models;

public class ErrorViewModel
{
    public int Codigo { get; set; } = 500;
    public string? RequestId { get; set; }

    public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);

    public (string Titulo, string Mensaje) Texto => Codigo switch
    {
        400 => ("Solicitud no válida", "Revisa los datos enviados e inténtalo de nuevo."),
        403 => ("Acceso denegado", "No tienes permisos para ver esta página."),
        404 => ("Página no encontrada", "La llamita buscó por todos lados, pero esta página no existe o fue movida."),
        429 => ("Demasiados intentos", "Espera un minuto antes de volver a intentarlo."),
        _ => ("Algo salió mal", "Ocurrió un error inesperado. Ya quedó registrado; inténtalo de nuevo en unos minutos.")
    };
}
