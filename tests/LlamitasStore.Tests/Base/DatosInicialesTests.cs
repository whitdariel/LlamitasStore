using LlamitasStore.Data;
using LlamitasStore.Data.Semilla;
using LlamitasStore.Models.Entities;
using LlamitasStore.Tests.Soporte;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LlamitasStore.Tests.Base;

public class DatosInicialesTests
{
    private static readonly Dictionary<string, string?> ConDemo = new()
    {
        ["Seed:DatosDemo"] = "true",
        ["Seed:PasswordAdmin"] = "Prueba12345",
        ["Seed:PasswordVendedor"] = "Prueba12345",
        ["Seed:PasswordCliente"] = "Prueba12345"
    };

    private static async Task EjecutarSemillaAsync(BaseDatosPrueba bd)
    {
        using var scope = bd.Servicios.CreateScope();
        var sp = scope.ServiceProvider;
        var semilla = new DatosIniciales(
            sp.GetRequiredService<AppDbContext>(),
            sp.GetRequiredService<RoleManager<Rol>>(),
            sp.GetRequiredService<UserManager<Usuario>>(),
            sp.GetRequiredService<IConfiguration>(),
            sp.GetRequiredService<ILogger<DatosIniciales>>());
        await semilla.EjecutarAsync();
    }

    [Fact]
    public async Task Ejecutar_dos_veces_no_duplica_datos()
    {
        await using var bd = await BaseDatosPrueba.CrearAsync(ConDemo);

        await EjecutarSemillaAsync(bd);
        await using var db = bd.NuevoContexto();
        var antes = (await db.Productos.CountAsync(), await db.Users.CountAsync(), await db.Permisos.CountAsync(), await db.Ajustes.CountAsync());

        await EjecutarSemillaAsync(bd);
        var despues = (await db.Productos.CountAsync(), await db.Users.CountAsync(), await db.Permisos.CountAsync(), await db.Ajustes.CountAsync());

        Assert.Equal(antes, despues);
        Assert.True(antes.Item1 >= 40, "El catálogo demo debería tener al menos 40 productos");
    }

    [Fact]
    public async Task Sin_datos_demo_solo_crea_roles_modulos_y_ajustes()
    {
        await using var bd = await BaseDatosPrueba.CrearAsync();

        await EjecutarSemillaAsync(bd);

        await using var db = bd.NuevoContexto();
        Assert.Equal(3, await db.Roles.CountAsync());
        Assert.Equal(Modulos.Todos.Length, await db.Modulos.CountAsync());
        Assert.Equal(0, await db.Productos.CountAsync());
        Assert.Equal(0, await db.Users.CountAsync());
    }

    [Fact]
    public async Task Contrasenas_demo_se_guardan_hasheadas()
    {
        await using var bd = await BaseDatosPrueba.CrearAsync(ConDemo);
        await EjecutarSemillaAsync(bd);

        await using var db = bd.NuevoContexto();
        var hashes = await db.Users.Select(u => u.PasswordHash).ToListAsync();

        Assert.Equal(3, hashes.Count);
        Assert.All(hashes, h =>
        {
            Assert.False(string.IsNullOrEmpty(h));
            Assert.DoesNotContain("Prueba12345", h);
        });
    }

    [Fact]
    public async Task Vendedor_no_tiene_permisos_sobre_usuarios_ni_configuracion()
    {
        await using var bd = await BaseDatosPrueba.CrearAsync();
        await EjecutarSemillaAsync(bd);

        await using var db = bd.NuevoContexto();
        var clavesVendedor = await db.Permisos
            .Where(p => p.Rol.Name == Roles.Vendedor)
            .Select(p => p.Modulo.Clave)
            .ToListAsync();

        Assert.DoesNotContain(Modulos.Usuarios, clavesVendedor);
        Assert.DoesNotContain(Modulos.Configuracion, clavesVendedor);
        Assert.Contains(Modulos.Pedidos, clavesVendedor);
    }

    [Fact]
    public async Task Componentes_del_pc_builder_tienen_atributos_de_compatibilidad()
    {
        await using var bd = await BaseDatosPrueba.CrearAsync(ConDemo);
        await EjecutarSemillaAsync(bd);

        await using var db = bd.NuevoContexto();
        var tiposConReglas = new[]
        {
            TipoComponente.Procesador, TipoComponente.PlacaMadre, TipoComponente.MemoriaRam, TipoComponente.TarjetaGrafica,
            TipoComponente.Almacenamiento, TipoComponente.FuentePoder, TipoComponente.Gabinete, TipoComponente.Refrigeracion
        };
        var sinCompat = await db.Productos
            .Where(p => tiposConReglas.Contains(p.Categoria.TipoComponente) && p.Compatibilidad == null)
            .Select(p => p.Sku)
            .ToListAsync();

        Assert.Empty(sinCompat);
    }
}
