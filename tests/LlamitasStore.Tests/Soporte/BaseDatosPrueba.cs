using LlamitasStore.Data;
using LlamitasStore.Models.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LlamitasStore.Tests.Soporte;

/// <summary>
/// AppDbContext sobre SQLite en memoria: aplica claves, índices únicos y CHECK reales.
/// SQLite guarda decimal como TEXT y compara mal en los CHECK, por eso aquí se convierte a double.
/// </summary>
public sealed class AppDbContextSqlite : AppDbContext
{
    public AppDbContextSqlite(DbContextOptions<AppDbContext> options) : base(options) { }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);
        configurationBuilder.Properties<decimal>().HaveConversion<double>();
        configurationBuilder.Properties<decimal?>().HaveConversion<double?>();
    }
}

/// <summary>Base de datos aislada por prueba, con contenedor de servicios (Identity incluido).</summary>
public sealed class BaseDatosPrueba : IAsyncDisposable
{
    private readonly SqliteConnection _conexion;
    public ServiceProvider Servicios { get; }

    private BaseDatosPrueba(SqliteConnection conexion, ServiceProvider servicios)
    {
        _conexion = conexion;
        Servicios = servicios;
    }

    public static async Task<BaseDatosPrueba> CrearAsync(Dictionary<string, string?>? ajustes = null)
    {
        var conexion = new SqliteConnection("DataSource=:memory:");
        await conexion.OpenAsync();

        var opciones = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(conexion).Options;

        var configuracion = new ConfigurationBuilder()
            .AddInMemoryCollection(ajustes ?? new Dictionary<string, string?>())
            .Build();

        var servicios = new ServiceCollection();
        servicios.AddLogging();
        servicios.AddSingleton<IConfiguration>(configuracion);
        servicios.AddScoped<AppDbContext>(_ => new AppDbContextSqlite(opciones));
        servicios.AddIdentityCore<Usuario>(LlamitasStore.Infraestructura.OpcionesIdentidad.Configurar)
            .AddRoles<Rol>()
            .AddEntityFrameworkStores<AppDbContext>();

        var proveedor = servicios.BuildServiceProvider();
        using (var scope = proveedor.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreatedAsync();

        return new BaseDatosPrueba(conexion, proveedor);
    }

    public AppDbContext NuevoContexto() => new AppDbContextSqlite(
        new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conexion).Options);

    public async ValueTask DisposeAsync()
    {
        await Servicios.DisposeAsync();
        await _conexion.DisposeAsync();
    }
}
