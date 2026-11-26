using System.Security.Claims;
using LlamitasStore.Data;
using LlamitasStore.Data.Semilla;
using LlamitasStore.Models.Entities;
using LlamitasStore.Services.Cuentas;
using LlamitasStore.Tests.Soporte;
using LlamitasStore.ViewModels;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LlamitasStore.Tests.Cuentas;

public sealed class ServiciosCuentaTests : IAsyncLifetime
{
    private BaseDatosPrueba _bd = null!;
    private int _ana, _beto;

    public async Task InitializeAsync()
    {
        _bd = await BaseDatosPrueba.CrearAsync();
        using var scope = _bd.Servicios.CreateScope();
        var sp = scope.ServiceProvider;
        await new DatosIniciales(sp.GetRequiredService<AppDbContext>(), sp.GetRequiredService<RoleManager<Rol>>(),
            sp.GetRequiredService<UserManager<Usuario>>(), sp.GetRequiredService<IConfiguration>(),
            sp.GetRequiredService<ILogger<DatosIniciales>>()).EjecutarAsync();

        await using var db = _bd.NuevoContexto();
        // Igual que los crea Identity: con SecurityStamp (obligatorio para vincular logins).
        var a = new Usuario { UserName = "ana@x.bo", Email = "ana@x.bo", NormalizedEmail = "ANA@X.BO", NormalizedUserName = "ANA@X.BO", Nombre = "Ana", Apellido = "A", SecurityStamp = Guid.NewGuid().ToString() };
        var b = new Usuario { UserName = "beto@x.bo", Email = "beto@x.bo", NormalizedEmail = "BETO@X.BO", NormalizedUserName = "BETO@X.BO", Nombre = "Beto", Apellido = "B", SecurityStamp = Guid.NewGuid().ToString() };
        db.AddRange(a, b);
        await db.SaveChangesAsync();
        (_ana, _beto) = (a.Id, b.Id);
    }

    public async Task DisposeAsync() => await _bd.DisposeAsync();

    private static DireccionVm Vm(string alias, bool predeterminada = false) => new()
    {
        Alias = alias, Destinatario = "D", Telefono = "71234567", Departamento = "La Paz", Ciudad = "Viacha",
        DireccionLinea = "Calle", EsPredeterminada = predeterminada
    };

    // ---------- Direcciones ----------

    [Fact]
    public async Task Primera_direccion_es_predeterminada_y_solo_puede_haber_una()
    {
        await using var db = _bd.NuevoContexto();
        var s = new ServicioDirecciones(db);

        await s.CrearAsync(_ana, Vm("Casa"));
        await s.CrearAsync(_ana, Vm("Oficina", predeterminada: true));
        await s.CrearAsync(_ana, Vm("Taller"));

        var lista = await s.ListarAsync(_ana);
        Assert.Equal("Oficina", Assert.Single(lista, d => d.EsPredeterminada).Alias);

        await s.MarcarPredeterminadaAsync(_ana, lista.Single(d => d.Alias == "Taller").Id);
        Assert.Equal("Taller", Assert.Single(await s.ListarAsync(_ana), d => d.EsPredeterminada).Alias);
    }

    [Fact]
    public async Task Borrar_la_predeterminada_asigna_otra()
    {
        await using var db = _bd.NuevoContexto();
        var s = new ServicioDirecciones(db);
        await s.CrearAsync(_ana, Vm("Casa"));
        await s.CrearAsync(_ana, Vm("Oficina"));

        var casa = (await s.ListarAsync(_ana)).Single(d => d.Alias == "Casa");
        Assert.Equal(ResultadoDireccion.Ok, await s.EliminarAsync(_ana, casa.Id));
        Assert.True(Assert.Single(await s.ListarAsync(_ana)).EsPredeterminada);
    }

    [Fact]
    public async Task Las_direcciones_de_otro_usuario_son_invisibles()
    {
        await using var db = _bd.NuevoContexto();
        var s = new ServicioDirecciones(db);
        await s.CrearAsync(_ana, Vm("Casa"));
        var id = (await s.ListarAsync(_ana)).Single().Id;

        Assert.Null(await s.ObtenerAsync(_beto, id));
        Assert.Equal(ResultadoDireccion.NoEncontrada, await s.EditarAsync(_beto, id, Vm("X")));
        Assert.Equal(ResultadoDireccion.NoEncontrada, await s.EliminarAsync(_beto, id));
        Assert.Equal(ResultadoDireccion.NoEncontrada, await s.MarcarPredeterminadaAsync(_beto, id));
        Assert.Single(await s.ListarAsync(_ana));
    }

    [Fact]
    public async Task Limite_de_diez_direcciones_y_departamento_valido()
    {
        await using var db = _bd.NuevoContexto();
        var s = new ServicioDirecciones(db);
        for (var i = 0; i < ServicioDirecciones.Maximo; i++) Assert.Equal(ResultadoDireccion.Ok, await s.CrearAsync(_ana, Vm($"D{i}")));

        Assert.Equal(ResultadoDireccion.LimiteAlcanzado, await s.CrearAsync(_ana, Vm("Una más")));
        var mala = Vm("Mala"); mala.Departamento = "Narnia";
        Assert.Equal(ResultadoDireccion.DepartamentoInvalido, await s.CrearAsync(_beto, mala));
    }

    // ---------- Favoritos ----------

    private async Task<(int Activo, int Inactivo)> CrearProductosAsync(AppDbContext db)
    {
        var c = new Categoria { Nombre = "C", Slug = "c" };
        var m = new Marca { Nombre = "M", Slug = "m" };
        var activo = new Producto { Sku = "A", Nombre = "A", Slug = "a", DescripcionCorta = "a", Descripcion = "a", Categoria = c, Marca = m, Precio = 10, PrecioCosto = 5, Stock = 1 };
        var inactivo = new Producto { Sku = "I", Nombre = "I", Slug = "i", DescripcionCorta = "i", Descripcion = "i", Categoria = c, Marca = m, Precio = 10, PrecioCosto = 5, Stock = 1, Estado = EstadoProducto.Inactivo };
        db.AddRange(activo, inactivo);
        await db.SaveChangesAsync();
        return (activo.Id, inactivo.Id);
    }

    [Fact]
    public async Task Favorito_se_agrega_y_quita_y_cuenta_por_usuario()
    {
        await using var db = _bd.NuevoContexto();
        var (activo, _) = await CrearProductosAsync(db);
        var s = new ServicioFavoritos(db);

        Assert.Equal(new ResultadoFavorito(true, true, 1), await s.AlternarAsync(_ana, activo));
        Assert.Equal(0, await s.ContarAsync(_beto));
        Assert.Equal(new ResultadoFavorito(true, false, 0), await s.AlternarAsync(_ana, activo));
    }

    [Fact]
    public async Task No_se_puede_agregar_un_producto_inactivo_o_inexistente()
    {
        await using var db = _bd.NuevoContexto();
        var (_, inactivo) = await CrearProductosAsync(db);
        var s = new ServicioFavoritos(db);

        Assert.False((await s.AlternarAsync(_ana, inactivo)).Encontrado);
        Assert.False((await s.AlternarAsync(_ana, 987654)).Encontrado);
        Assert.Equal(0, await s.ContarAsync(_ana));
    }

    // ---------- Login con Google ----------

    private static ClaimsPrincipal Google(string email, bool verificado = true, string nombre = "Gabi") =>
        new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.Email, email), new Claim(ClaimTypes.GivenName, nombre), new Claim(ClaimTypes.Surname, "Google"),
            new Claim("email_verified", verificado ? "true" : "false")
        ], "Google"));

    private ServicioLoginExterno Externo(IServiceScope scope) =>
        new(scope.ServiceProvider.GetRequiredService<UserManager<Usuario>>(), NullLogger<ServicioLoginExterno>.Instance);

    [Fact]
    public async Task Google_registra_cliente_nuevo_y_la_segunda_vez_reconoce_la_cuenta()
    {
        using var scope = _bd.Servicios.CreateScope();
        var s = Externo(scope);

        var r1 = await s.VincularORegistrarAsync(Google("gabi@gmail.com"), "Google", "sub-123", "Google");
        Assert.True(r1.EsNuevo);
        var um = scope.ServiceProvider.GetRequiredService<UserManager<Usuario>>();
        Assert.True(await um.IsInRoleAsync(r1.Usuario!, Roles.Cliente));
        Assert.False(await um.HasPasswordAsync(r1.Usuario!));

        var r2 = await s.VincularORegistrarAsync(Google("gabi@gmail.com"), "Google", "sub-123", "Google");
        Assert.False(r2.EsNuevo);
        Assert.Equal(r1.Usuario!.Id, r2.Usuario!.Id);
    }

    [Fact]
    public async Task Google_vincula_cuenta_existente_solo_si_el_correo_esta_verificado()
    {
        using var scope = _bd.Servicios.CreateScope();
        var s = Externo(scope);

        var sinVerificar = await s.VincularORegistrarAsync(Google("ana@x.bo", verificado: false), "Google", "sub-ana", "Google");
        Assert.Null(sinVerificar.Usuario);
        Assert.Contains("no está verificado", sinVerificar.Error);

        var verificado = await s.VincularORegistrarAsync(Google("ana@x.bo"), "Google", "sub-ana", "Google");
        Assert.Equal(_ana, verificado.Usuario!.Id);
        Assert.False(verificado.EsNuevo);
    }

    [Fact]
    public async Task Google_rechaza_cuentas_desactivadas()
    {
        await using (var db = _bd.NuevoContexto())
        {
            var beto = await db.Users.SingleAsync(u => u.Id == _beto);
            beto.Estado = EstadoUsuario.Suspendido;
            await db.SaveChangesAsync();
        }
        using var scope = _bd.Servicios.CreateScope();
        var r = await Externo(scope).VincularORegistrarAsync(Google("beto@x.bo"), "Google", "sub-beto", "Google");
        Assert.Null(r.Usuario);
        Assert.Contains("desactivada", r.Error);
    }
}
