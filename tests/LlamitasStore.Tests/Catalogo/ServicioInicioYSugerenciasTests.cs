using LlamitasStore.Data;
using LlamitasStore.Models.Entities;
using LlamitasStore.Services.Catalogo;
using LlamitasStore.Tests.Soporte;

namespace LlamitasStore.Tests.Catalogo;

public sealed class ServicioInicioYSugerenciasTests : IAsyncLifetime
{
    private static readonly DateTime Hoy = new(2026, 11, 26, 12, 0, 0, DateTimeKind.Utc);
    private BaseDatosPrueba _bd = null!;

    public async Task InitializeAsync()
    {
        _bd = await BaseDatosPrueba.CrearAsync();
        await using var db = _bd.NuevoContexto();

        var cpu = new Categoria { Nombre = "Procesadores", Slug = "procesadores", Icono = "cpu", Orden = 1 };
        var ratones = new Categoria { Nombre = "Ratones", Slug = "ratones", Icono = "raton", Orden = 2 };
        var amd = new Marca { Nombre = "AMD", Slug = "amd", Destacada = true };
        var logi = new Marca { Nombre = "Logitech", Slug = "logitech", Destacada = true };
        var sinProductos = new Marca { Nombre = "Vacía", Slug = "vacia", Destacada = true };

        db.AddRange(sinProductos,
            P("A", "AMD Ryzen 5 7600", cpu, amd, 1000, 900, 5, destacado: true),          // 10% oferta
            P("B", "AMD Ryzen 7 7800X3D", cpu, amd, 2000, 1500, 0, destacado: true),     // 25% pero SIN stock
            P("C", "AMD Ryzen 9 Prueba", cpu, amd, 3000, null, 2, nuevo: true),
            P("D", "Logitech G502 HERO", ratones, logi, 400, null, 10),                  // 30% por promoción
            P("E", "Logitech Inactivo", ratones, logi, 100, 50, 10, estado: EstadoProducto.Inactivo));

        db.Promociones.Add(new Promocion { Nombre = "Ratones", Porcentaje = 30, FechaInicio = Hoy.AddDays(-1), FechaFin = Hoy.AddDays(3), Categoria = ratones });
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _bd.DisposeAsync();

    private static Producto P(string sku, string nombre, Categoria c, Marca m, decimal precio, decimal? oferta, int stock,
        bool destacado = false, bool nuevo = false, EstadoProducto estado = EstadoProducto.Activo) => new()
    {
        Sku = sku, Nombre = nombre, Slug = sku.ToLowerInvariant(), Categoria = c, Marca = m, Precio = precio, PrecioOferta = oferta,
        PrecioCosto = 1, Stock = stock, Destacado = destacado, EsNuevo = nuevo, Estado = estado,
        DescripcionCorta = nombre, Descripcion = nombre
    };

    private (ServicioInicio Inicio, ServicioCatalogo Catalogo, AppDbContext Db) Servicios()
    {
        var db = _bd.NuevoContexto();
        var precios = new ServicioPrecios(db, new RelojFijo(Hoy));
        var catalogo = new ServicioCatalogo(db, precios);
        return (new ServicioInicio(db, catalogo, precios), catalogo, db);
    }

    [Fact]
    public async Task Ofertas_ordenadas_por_mayor_descuento_y_solo_con_stock()
    {
        var (inicio, _, db) = Servicios();
        await using (db)
        {
            var vm = await inicio.ObtenerAsync();
            Assert.Equal(["D", "A"], vm.Ofertas.Select(p => p.Sku).ToArray()); // 30% > 10%; B no tiene stock; E inactivo
        }
    }

    [Fact]
    public async Task Mas_vendidos_queda_vacio_sin_ventas_reales_y_novedades_prioriza_nuevos()
    {
        var (inicio, _, db) = Servicios();
        await using (db)
        {
            var vm = await inicio.ObtenerAsync();
            Assert.Empty(vm.MasVendidos);
            Assert.Equal("C", vm.Novedades[0].Sku);
            Assert.Equal(["A"], vm.Destacados.Select(p => p.Sku).ToArray()); // B es destacado pero está agotado
        }
    }

    [Fact]
    public async Task Marcas_destacadas_sin_productos_activos_no_se_muestran_y_totales_son_reales()
    {
        var (inicio, _, db) = Servicios();
        await using (db)
        {
            var vm = await inicio.ObtenerAsync();
            Assert.Equal(["AMD", "Logitech"], vm.Marcas.Select(m => m.Nombre).ToArray());
            Assert.Equal(4, vm.TotalProductos);
            Assert.Equal(2, vm.TotalMarcas);
            Assert.Equal("cat-cpu", vm.Categorias[0].IconoSprite);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("a")]
    public async Task Sugerencias_requieren_al_menos_dos_caracteres(string? q)
    {
        var (_, catalogo, db) = Servicios();
        await using (db)
        {
            var s = await catalogo.SugerirAsync(q);
            Assert.Empty(s.Productos);
            Assert.Empty(s.Categorias);
        }
    }

    [Fact]
    public async Task Sugerencias_incluyen_categorias_y_productos_activos_con_precio_final()
    {
        var (_, catalogo, db) = Servicios();
        await using (db)
        {
            var s = await catalogo.SugerirAsync("rat");
            Assert.Equal(["ratones"], s.Categorias.Select(c => c.Slug).ToArray());

            var logi = await catalogo.SugerirAsync("logitech");
            var g502 = Assert.Single(logi.Productos); // el inactivo no aparece
            Assert.Equal(280m, g502.Precio.PrecioFinal); // 400 - 30%
        }
    }

    [Fact]
    public async Task Sugerencias_ponen_primero_los_que_empiezan_con_el_texto_y_respetan_el_limite()
    {
        var (_, catalogo, db) = Servicios();
        await using (db)
        {
            var s = await catalogo.SugerirAsync("ryzen", maxProductos: 2);
            Assert.Equal(2, s.Productos.Count);
            Assert.All(s.Productos, p => Assert.Contains("Ryzen", p.Nombre));

            var amd = await catalogo.SugerirAsync("amd ryzen 9");
            Assert.Equal("C", Assert.Single(amd.Productos).Sku);
        }
    }
}
