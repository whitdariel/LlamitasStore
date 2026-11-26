namespace LlamitasStore.Infraestructura;

/// <summary>Cabeceras HTTP de endurecimiento básico contra clickjacking, sniffing de MIME y fugas de referer.</summary>
public static class CabecerasSeguridad
{
    public static IApplicationBuilder UseCabecerasSeguridad(this IApplicationBuilder app) =>
        app.Use(async (contexto, siguiente) =>
        {
            var h = contexto.Response.Headers;
            h["X-Content-Type-Options"] = "nosniff";
            h["X-Frame-Options"] = "DENY";
            h["Referrer-Policy"] = "strict-origin-when-cross-origin";
            h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
            // Solo recursos propios: scripts y estilos se sirven desde wwwroot. Las imágenes de Google (avatar) se permiten.
            h["Content-Security-Policy"] =
                "default-src 'self'; " +
                "img-src 'self' data: https://lh3.googleusercontent.com; " +
                "style-src 'self'; " +
                "script-src 'self'; " +
                "font-src 'self'; " +
                "form-action 'self' https://accounts.google.com; " +
                "frame-ancestors 'none'; " +
                "base-uri 'self'; " +
                "object-src 'none'";
            await siguiente();
        });
}
