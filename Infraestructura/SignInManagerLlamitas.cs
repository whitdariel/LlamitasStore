using LlamitasStore.Models.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace LlamitasStore.Infraestructura;

/// <summary>Impide iniciar sesión (con contraseña o con Google) a usuarios inactivos o suspendidos.</summary>
public class SignInManagerLlamitas : SignInManager<Usuario>
{
    public SignInManagerLlamitas(UserManager<Usuario> userManager, IHttpContextAccessor contextAccessor,
        IUserClaimsPrincipalFactory<Usuario> claimsFactory, IOptions<IdentityOptions> optionsAccessor,
        ILogger<SignInManager<Usuario>> logger, IAuthenticationSchemeProvider schemes, IUserConfirmation<Usuario> confirmation)
        : base(userManager, contextAccessor, claimsFactory, optionsAccessor, logger, schemes, confirmation) { }

    public override async Task<bool> CanSignInAsync(Usuario user)
    {
        if (user.Estado != EstadoUsuario.Activo) return false;
        return await base.CanSignInAsync(user);
    }
}

/// <summary>Agrega el nombre visible al cookie de sesión para mostrarlo en el header sin consultar la BD.</summary>
public class UsuarioClaimsFactory : UserClaimsPrincipalFactory<Usuario, Rol>
{
    public const string ClaimNombre = "llamitas:nombre";

    public UsuarioClaimsFactory(UserManager<Usuario> userManager, RoleManager<Rol> roleManager, IOptions<IdentityOptions> options)
        : base(userManager, roleManager, options) { }

    protected override async Task<System.Security.Claims.ClaimsIdentity> GenerateClaimsAsync(Usuario user)
    {
        var identidad = await base.GenerateClaimsAsync(user);
        identidad.AddClaim(new System.Security.Claims.Claim(ClaimNombre, user.Nombre));
        return identidad;
    }
}
