using System.Threading.RateLimiting;
using LlamitasStore.Data;
using LlamitasStore.Data.Semilla;
using LlamitasStore.Infraestructura;
using LlamitasStore.Models.Entities;
using LlamitasStore.Services;
using LlamitasStore.Services.Catalogo;
using LlamitasStore.Services.Correo;
using LlamitasStore.Services.Cuentas;
using LlamitasStore.Services.Ventas;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// ---------- Base de datos (MySQL / MariaDB) ----------
var cadena = config.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(cadena))
    throw new InvalidOperationException(
        "Falta ConnectionStrings:DefaultConnection. Configúrala en appsettings.Development.json, user-secrets o variables de entorno.");

// Versión fija en configuración: AutoDetect abría una conexión al arrancar y la app no iniciaba si MySQL estaba apagado.
var versionServidor = ServerVersion.Parse(config["Database:ServerVersion"] ?? "10.11.19-mariadb");

builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseMySql(cadena, versionServidor, mysql => mysql.EnableRetryOnFailure(3)));

// ---------- Identidad ----------
builder.Services
    .AddIdentity<Usuario, Rol>(OpcionesIdentidad.Configurar)
    .AddEntityFrameworkStores<AppDbContext>()
    .AddSignInManager<SignInManagerLlamitas>()
    .AddClaimsPrincipalFactory<UsuarioClaimsFactory>()
    .AddDefaultTokenProviders()
    .AddErrorDescriber<ErroresIdentidadEs>();

// Enlaces de recuperación de contraseña: válidos 3 horas (y de un solo uso: al cambiar la clave cambia el SecurityStamp).
builder.Services.Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = TimeSpan.FromHours(3));

// Si un admin desactiva a un usuario, se cambia su SecurityStamp y la sesión cae en <= 5 minutos.
builder.Services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.FromMinutes(5));

// Producción: cookies solo por HTTPS. Desarrollo: también por http://localhost (el certificado local puede no ser de confianza).
var politicaCookies = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;

builder.Services.ConfigureApplicationCookie(o =>
{
    o.LoginPath = "/cuenta/login";
    o.LogoutPath = "/cuenta/logout";
    o.AccessDeniedPath = "/cuenta/acceso-denegado";
    o.Cookie.Name = ".LlamitasStore.Sesion";
    o.Cookie.HttpOnly = true;
    o.Cookie.SecurePolicy = politicaCookies;
    o.Cookie.SameSite = SameSiteMode.Lax;
    o.ExpireTimeSpan = TimeSpan.FromDays(14);
    o.SlidingExpiration = true;
});

var autenticacion = builder.Services.AddAuthentication();
var googleId = config["Authentication:Google:ClientId"];
var googleSecreto = config["Authentication:Google:ClientSecret"];
if (!string.IsNullOrWhiteSpace(googleId) && !string.IsNullOrWhiteSpace(googleSecreto))
{
    autenticacion.AddGoogle(o =>
    {
        o.ClientId = googleId;
        o.ClientSecret = googleSecreto;
        o.ClaimActions.MapJsonKey("email_verified", "email_verified");
        o.ClaimActions.MapJsonKey("picture", "picture");
        o.Events.OnRemoteFailure = ctx =>
        {
            ctx.Response.Redirect("/cuenta/login?error=externo");
            ctx.HandleResponse();
            return Task.CompletedTask;
        };
    });
}

builder.Services.AddAuthorization();
// Panel administrativo: permisos por módulo y acción (matriz rol × módulo), verificados en el servidor.
builder.Services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationPolicyProvider, ProveedorPoliticasPermiso>();
builder.Services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, ManejadorPermiso>();
builder.Services.AddScoped<ServicioPermisos>();
builder.Services.AddScoped<LlamitasStore.Services.Admin.ServicioDashboard>();
builder.Services.AddScoped<LlamitasStore.Services.Admin.ServicioInventario>();
builder.Services.AddScoped<LlamitasStore.Services.Admin.ServicioAdminPedidos>();
builder.Services.AddScoped<LlamitasStore.Services.Admin.ServicioAdminUsuarios>();
builder.Services.AddScoped<LlamitasStore.Services.Admin.ServicioAdminProductos>();

// ---------- MVC, antiforgery y límites ----------
builder.Services.AddControllersWithViews(o =>
{
    // Todo POST/PUT/DELETE exige token CSRF aunque se olvide el atributo.
    o.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
});
builder.Services.AddAntiforgery(o =>
{
    o.Cookie.Name = ".LlamitasStore.Csrf";
    o.Cookie.SecurePolicy = politicaCookies;
    o.HeaderName = "X-CSRF-TOKEN";
});
builder.Services.AddRouting(o => o.LowercaseUrls = true);

// Español sin entidades: "contraseña" se escribe tal cual en vez de "contrase&#xF1;a".
// Los caracteres peligrosos para XSS (< > & " ') se siguen codificando siempre.
builder.Services.AddWebEncoders(o => o.TextEncoderSettings = new System.Text.Encodings.Web.TextEncoderSettings(System.Text.Unicode.UnicodeRanges.All));

var limiteFormularios = config.GetValue("Limites:FormulariosPorMinuto", 10);
var limiteBusqueda = config.GetValue("Limites:BusquedasPorMinuto", 90);
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    // Login, registro, recuperación y contacto: N envíos por minuto por IP.
    o.AddPolicy(PoliticasLimite.Formularios, ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "desconocida",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = limiteFormularios, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    // Sugerencias del buscador (se piden mientras se escribe).
    o.AddPolicy(PoliticasLimite.Busqueda, ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "desconocida",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = limiteBusqueda, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

builder.Services.AddScoped<DatosIniciales>();

// ---------- Servicios de la tienda ----------
builder.Services.AddMemoryCache();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<AjustesTienda>();
builder.Services.AddScoped<ServicioPrecios>();
builder.Services.AddScoped<ServicioCatalogo>();
builder.Services.AddScoped<ServicioInicio>();

// ---------- Cuentas de usuario ----------
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ServicioDirecciones>();
builder.Services.AddScoped<ServicioFavoritos>();
builder.Services.AddScoped<FavoritosDelUsuario>();
builder.Services.AddScoped<ServicioLoginExterno>();
// ---------- Carrito y pedidos ----------
builder.Services.AddScoped<ServicioCupones>();
builder.Services.AddScoped<ServicioCarrito>();
builder.Services.AddScoped<CarritoActual>();
builder.Services.AddScoped<ServicioPedidos>();
builder.Services.AddScoped<LlamitasStore.Services.PcBuilder.ServicioPcBuilder>();
// ---------- Comunidad y extras ----------
builder.Services.AddScoped<ServicioComparador>();
builder.Services.AddScoped<ComparadorActual>();
builder.Services.AddScoped<LlamitasStore.Services.Comunidad.ServicioResenas>();
builder.Services.AddScoped<LlamitasStore.Services.Comunidad.ServicioNotificaciones>();
builder.Services.AddScoped<LlamitasStore.Services.Comunidad.ServicioContacto>();

builder.Services.Configure<OpcionesCorreo>(config.GetSection("Correo"));
builder.Services.AddSingleton<PlantillasCorreo>();
if (!string.IsNullOrWhiteSpace(config["Correo:Smtp:Host"]))
    builder.Services.AddSingleton<IEnviadorCorreo, EnviadorSmtp>();
else if (builder.Environment.IsDevelopment())
    builder.Services.AddSingleton<IEnviadorCorreo, EnviadorArchivo>();
else
    builder.Services.AddSingleton<IEnviadorCorreo, EnviadorNoConfigurado>();

var app = builder.Build();

// ---------- Migraciones y datos iniciales ----------
if (config.GetValue("Database:MigrarAlIniciar", false))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<DatosIniciales>().EjecutarAsync();
}

// ---------- Pipeline ----------
// Detrás de un proxy o túnel (Cloudflare Tunnel, Nginx, IIS): toma el esquema https, la IP real del visitante y el
// dominio público de las cabeceras X-Forwarded-*. Por defecto solo se confía en proxies que se conectan desde esta
// misma máquina (loopback), así nadie de afuera puede falsificar su IP o el esquema.
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
                       | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto
                       | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedHost
});

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/error/500");
    app.UseHsts();
}

// Cultura invariante para leer formularios: los <input type="number"> envían "1590.50" con punto en cualquier
// navegador. El dinero se muestra siempre formateado explícitamente en es-BO (AjustesTienda.Dinero).
app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new Microsoft.AspNetCore.Localization.RequestCulture(System.Globalization.CultureInfo.InvariantCulture),
    SupportedCultures = [System.Globalization.CultureInfo.InvariantCulture],
    SupportedUICultures = [System.Globalization.CultureInfo.InvariantCulture]
});

app.UseStatusCodePagesWithReExecute("/error/{0}");
app.UseHttpsRedirection();
app.UseCabecerasSeguridad();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
        name: "areas",
        pattern: "{area:exists}/{controller=Dashboard}/{action=Index}/{id?}")
    .WithStaticAssets();

app.MapControllerRoute(
        name: "default",
        pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();

/// <summary>Expuesto para pruebas de integración (WebApplicationFactory).</summary>
public partial class Program;
