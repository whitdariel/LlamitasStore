using Microsoft.AspNetCore.Identity;

namespace LlamitasStore.Infraestructura;

/// <summary>Política de cuentas única para la app y para las pruebas.</summary>
public static class OpcionesIdentidad
{
    public static void Configurar(IdentityOptions o)
    {
        o.User.RequireUniqueEmail = true;
        o.Password.RequiredLength = 8;
        o.Password.RequireDigit = true;
        o.Password.RequireLowercase = true;
        o.Password.RequireUppercase = true;
        o.Password.RequireNonAlphanumeric = false;
        o.Lockout.MaxFailedAccessAttempts = 5;
        o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        o.Lockout.AllowedForNewUsers = true;
        o.SignIn.RequireConfirmedAccount = false;
    }
}
