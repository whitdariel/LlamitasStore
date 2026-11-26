using System.Threading.RateLimiting;
using LlamitasStore.Data;
using LlamitasStore.Data.Semilla;
using LlamitasStore.Infraestructura;
using LlamitasStore.Models.Entities;
using LlamitasStore.Services;
using LlamitasStore.Services.Catalogo;
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

// Si un admin desactiva a un usuario, se cambia su SecurityStamp y la sesión cae en <= 5 minutos.
builder.Services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.FromMinutes(5));

builder.Services.ConfigureApplicationCookie(o =>
{
    o.LoginPath = "/cuenta/login";
    o.LogoutPath = "/cuenta/logout";
    o.AccessDeniedPath = "/cuenta/acceso-denegado";
    o.Cookie.Name = ".LlamitasStore.Sesion";
    o.Cookie.HttpOnly = true;
    o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
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
            ctx.Response.Redirect("/cuenta/login?error=google");
            ctx.HandleResponse();
            return Task.CompletedTask;
        };
    });
}

builder.Services.AddAuthorization();

// ---------- MVC, antiforgery y límites ----------
builder.Services.AddControllersWithViews(o =>
{
    // Todo POST/PUT/DELETE exige token CSRF aunque se olvide el atributo.
    o.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
});
builder.Services.AddAntiforgery(o =>
{
    o.Cookie.Name = ".LlamitasStore.Csrf";
    o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    o.HeaderName = "X-CSRF-TOKEN";
});
builder.Services.AddRouting(o => o.LowercaseUrls = true);

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    // Login, registro, recuperación y contacto: 10 envíos por minuto por IP.
    o.AddPolicy(PoliticasLimite.Formularios, ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "desconocida",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    // Sugerencias del buscador (se piden mientras se escribe): 90 por minuto por IP.
    o.AddPolicy(PoliticasLimite.Busqueda, ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "desconocida",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 90, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

builder.Services.AddScoped<DatosIniciales>();

// ---------- Servicios de la tienda ----------
builder.Services.AddMemoryCache();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<AjustesTienda>();
builder.Services.AddScoped<ServicioPrecios>();
builder.Services.AddScoped<ServicioCatalogo>();
builder.Services.AddScoped<ServicioInicio>();

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
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/error/500");
    app.UseHsts();
}

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
