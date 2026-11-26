using System.Security.Claims;
using LlamitasStore.Models.Entities;
using Microsoft.AspNetCore.Identity;

namespace LlamitasStore.Services.Cuentas;

public record ResultadoExterno(Usuario? Usuario, string? Error, bool EsNuevo = false);

/// <summary>
/// Registro / vinculación de cuentas que entran con un proveedor externo (Google).
/// Regla de seguridad: una cuenta existente solo se vincula por correo si el proveedor confirma que el correo está verificado.
/// </summary>
public class ServicioLoginExterno
{
    private readonly UserManager<Usuario> _usuarios;
    private readonly ILogger<ServicioLoginExterno> _logger;

    public ServicioLoginExterno(UserManager<Usuario> usuarios, ILogger<ServicioLoginExterno> logger)
    {
        _usuarios = usuarios;
        _logger = logger;
    }

    public async Task<ResultadoExterno> VincularORegistrarAsync(ClaimsPrincipal externo, string proveedor, string claveProveedor, string nombreProveedor)
    {
        var porLogin = await _usuarios.FindByLoginAsync(proveedor, claveProveedor);
        if (porLogin is not null)
            return porLogin.Estado == EstadoUsuario.Activo
                ? new ResultadoExterno(porLogin, null)
                : new ResultadoExterno(null, "Tu cuenta está desactivada. Contacta a la tienda.");

        var correo = externo.FindFirstValue(ClaimTypes.Email)?.Trim();
        if (string.IsNullOrEmpty(correo))
            return new ResultadoExterno(null, $"{nombreProveedor} no compartió tu correo. Revisa los permisos e intenta de nuevo.");

        var verificado = string.Equals(externo.FindFirstValue("email_verified"), "true", StringComparison.OrdinalIgnoreCase);
        if (!verificado)
            return new ResultadoExterno(null, $"Tu correo de {nombreProveedor} no está verificado.");

        var existente = await _usuarios.FindByEmailAsync(correo);
        if (existente is not null)
        {
            if (existente.Estado != EstadoUsuario.Activo)
                return new ResultadoExterno(null, "Tu cuenta está desactivada. Contacta a la tienda.");

            var vinculo = await _usuarios.AddLoginAsync(existente, new UserLoginInfo(proveedor, claveProveedor, nombreProveedor));
            if (!vinculo.Succeeded) return new ResultadoExterno(null, Errores(vinculo));

            _logger.LogInformation("Cuenta {Correo} vinculada con {Proveedor}", correo, proveedor);
            return new ResultadoExterno(existente, null);
        }

        var nombre = externo.FindFirstValue(ClaimTypes.GivenName) ?? externo.FindFirstValue(ClaimTypes.Name) ?? correo.Split('@')[0];
        var usuario = new Usuario
        {
            UserName = correo,
            Email = correo,
            EmailConfirmed = true,
            Nombre = Recortar(nombre, 60),
            Apellido = Recortar(externo.FindFirstValue(ClaimTypes.Surname) ?? string.Empty, 60),
            FotoUrl = externo.FindFirstValue("picture") is { Length: <= 500 } foto && foto.StartsWith("https://") ? foto : null,
            Estado = EstadoUsuario.Activo
        };

        var creado = await _usuarios.CreateAsync(usuario);
        if (!creado.Succeeded) return new ResultadoExterno(null, Errores(creado));

        await _usuarios.AddToRoleAsync(usuario, Roles.Cliente);
        await _usuarios.AddLoginAsync(usuario, new UserLoginInfo(proveedor, claveProveedor, nombreProveedor));
        _logger.LogInformation("Nuevo cliente registrado con {Proveedor}: {Correo}", proveedor, correo);
        return new ResultadoExterno(usuario, null, EsNuevo: true);
    }

    private static string Recortar(string v, int max) => v.Length <= max ? v : v[..max];
    private static string Errores(IdentityResult r) => string.Join(" ", r.Errors.Select(e => e.Description));
}
