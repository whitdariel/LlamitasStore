using LlamitasStore.Data;
using LlamitasStore.Models.Entities;
using LlamitasStore.Services;
using LlamitasStore.Services.Catalogo;
using LlamitasStore.Services.Ventas;
using LlamitasStore.Tests.Soporte;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace LlamitasStore.Tests.Ventas;

/// <summary>
/// Datos comunes de ventas:
///   A: 1000 Bs, stock 5 · B: 200 Bs, stock 1 · C: inactivo · D: agotado
///   Cupones: P10 (10%, mín. 300, 1 por usuario) · F50 (Bs 50, mín. 100, 1 uso total) · VIEJO (vencido)
///   Envío Bs 25, gratis desde Bs 1500. Usuarios: Ana y Beto.
/// </summary>
public sealed class EscenarioVentas : IAsyncDisposable
{
    public static readonly DateTime Hoy = new(2026, 11, 26, 12, 0, 0, DateTimeKind.Utc);
    public BaseDatosPrueba Bd { get; private init; } = null!;
    public int A, B, C, D, Ana, Beto;

    public static async Task<EscenarioVentas> CrearAsync()
    {
        var e = new EscenarioVentas { Bd = await BaseDatosPrueba.CrearAsync() };
        await using var db = e.Bd.NuevoContexto();

        db.Ajustes.AddRange(
            new AjusteTienda { Clave = ClavesAjuste.CostoEnvio, Valor = "25.00" },
            new AjusteTienda { Clave = ClavesAjuste.EnvioGratisDesde, Valor = "1500.00" });

        var cat = new Categoria { Nombre = "Cat", Slug = "cat" };
        var mar = new Marca { Nombre = "Mar", Slug = "mar" };
        Producto P(string sku, decimal precio, int stock, EstadoProducto estado = EstadoProducto.Activo) => new()
        {
            Sku = sku, Nombre = "Producto " + sku, Slug = sku.ToLowerInvariant(), DescripcionCorta = "c", Descripcion = "d",
            Categoria = cat, Marca = mar, Precio = precio, PrecioCosto = 1, Stock = stock, Estado = estado
        };
        var (a, b, c, d) = (P("A", 1000, 5), P("B", 200, 1), P("C", 100, 9, EstadoProducto.Inactivo), P("D", 100, 0));
        db.Productos.AddRange(a, b, c, d);

        var ana = new Usuario { UserName = "ana@x.bo", Email = "ana@x.bo", Nombre = "Ana", Apellido = "A", SecurityStamp = "s1" };
        var beto = new Usuario { UserName = "beto@x.bo", Email = "beto@x.bo", Nombre = "Beto", Apellido = "B", SecurityStamp = "s2" };
        db.Users.AddRange(ana, beto);

        db.Cupones.AddRange(
            new Cupon { Codigo = "P10", Tipo = TipoDescuento.Porcentaje, Valor = 10, MontoMinimo = 300, UsosPorUsuario = 1, FechaInicio = Hoy.AddDays(-1), FechaFin = Hoy.AddDays(30) },
            new Cupon { Codigo = "F50", Tipo = TipoDescuento.MontoFijo, Valor = 50, MontoMinimo = 100, UsosMaximos = 1, FechaInicio = Hoy.AddDays(-1), FechaFin = Hoy.AddDays(30) },
            new Cupon { Codigo = "VIEJO", Tipo = TipoDescuento.Porcentaje, Valor = 50, FechaInicio = Hoy.AddDays(-30), FechaFin = Hoy.AddDays(-1) });
        await db.SaveChangesAsync();

        db.Direcciones.AddRange(
            new Direccion { UsuarioId = ana.Id, Alias = "Casa", Destinatario = "Ana", Telefono = "71234567", Departamento = "La Paz", Ciudad = "Viacha", DireccionLinea = "Calle 1", EsPredeterminada = true },
            new Direccion { UsuarioId = beto.Id, Alias = "Casa", Destinatario = "Beto", Telefono = "71234568", Departamento = "Oruro", Ciudad = "Oruro", DireccionLinea = "Calle 2", EsPredeterminada = true });
        await db.SaveChangesAsync();

        (e.A, e.B, e.C, e.D, e.Ana, e.Beto) = (a.Id, b.Id, c.Id, d.Id, ana.Id, beto.Id);
        return e;
    }

    /// <summary>Servicios reales sobre un contexto nuevo (como en una petición HTTP).</summary>
    public (AppDbContext Db, ServicioCarrito Carrito, ServicioPedidos Pedidos, ServicioCupones Cupones) Servicios()
    {
        var db = Bd.NuevoContexto();
        var reloj = new RelojFijo(Hoy);
        var precios = new ServicioPrecios(db, reloj);
        var cupones = new ServicioCupones(db, reloj);
        var ajustes = new AjustesTienda(db, new MemoryCache(new MemoryCacheOptions()));
        return (db, new ServicioCarrito(db, precios, cupones, ajustes),
            new ServicioPedidos(db, precios, cupones, ajustes, reloj, NullLogger<ServicioPedidos>.Instance), cupones);
    }

    public int DireccionDe(int usuarioId)
    {
        using var db = Bd.NuevoContexto();
        return db.Direcciones.Single(d => d.UsuarioId == usuarioId).Id;
    }

    public async ValueTask DisposeAsync() => await Bd.DisposeAsync();
}
