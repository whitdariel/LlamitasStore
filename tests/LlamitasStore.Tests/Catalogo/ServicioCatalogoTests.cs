using LlamitasStore.Data;
using LlamitasStore.Models.Entities;
using LlamitasStore.Services.Catalogo;
using LlamitasStore.Tests.Soporte;
using LlamitasStore.ViewModels;

namespace LlamitasStore.Tests.Catalogo;

/// <summary>
/// Catálogo de prueba:
///   cpu:     R5 (AMD, 1000, oferta 900, stock 5, AM5/DDR5) · R7 (AMD, 2000, stock 0, AM5/DDR5)
///            I5 (Intel, 1500, stock 3, LGA1700, DDR4+DDR5) · Viejo (Intel, INACTIVO) · Retro (AMD, DESCONTINUADO)
///   ratones: G502 (Logitech, 400, stock 10) con promo vigente de 25% en la categoría → 300
///   Promo vencida de 50% en cpu (no debe aplicarse).
/// </summary>
public sealed class ServicioCatalogoTests : IAsyncLifetime
{
    private static readonly DateTime Hoy = new(2026, 11, 26, 12, 0, 0, DateTimeKind.Utc);
    private BaseDatosPrueba _bd = null!;

    public async Task InitializeAsync()
    {
        _bd = await BaseDatosPrueba.CrearAsync();
        await using var db = _bd.NuevoContexto();

        var cpu = new Categoria { Nombre = "Procesadores", Slug = "procesadores", TipoComponente = TipoComponente.Procesador, Orden = 1 };
        var ratones = new Categoria { Nombre = "Ratones", Slug = "ratones", Orden = 2 };
        var amd = new Marca { Nombre = "AMD", Slug = "amd" };
        var intel = new Marca { Nombre = "Intel", Slug = "intel" };
        var logi = new Marca { Nombre = "Logitech", Slug = "logitech" };

        db.AddRange(
            P("CPU-R5", "AMD Ryzen 5 7600", cpu, amd, 1000, 900, 5, "AM5", "DDR5", destacado: true),
            P("CPU-R7", "AMD Ryzen 7 7800X3D", cpu, amd, 2000, null, 0, "AM5", "DDR5"),
            P("CPU-I5", "Intel Core i5-14600K", cpu, intel, 1500, null, 3, "LGA1700", "DDR4,DDR5"),
            P("CPU-OLD", "Intel Pentium Viejo", cpu, intel, 100, null, 9, "LGA1151", "DDR4", estado: EstadoProducto.Inactivo),
            P("CPU-RETRO", "AMD Athlon Retro", cpu, amd, 300, null, 0, "AM4", "DDR4", estado: EstadoProducto.Descontinuado),
            P("MOU-G502", "Logitech G502 HERO", ratones, logi, 400, null, 10));

        db.Promociones.AddRange(
            new Promocion { Nombre = "Semana gamer", Porcentaje = 25, FechaInicio = Hoy.AddDays(-1), FechaFin = Hoy.AddDays(5), Categoria = ratones },
            new Promocion { Nombre = "Vencida", Porcentaje = 50, FechaInicio = Hoy.AddDays(-20), FechaFin = Hoy.AddDays(-2), Categoria = cpu });

        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _bd.DisposeAsync();

    private static Producto P(string sku, string nombre, Categoria c, Marca m, decimal precio, decimal? oferta, int stock,
        string? socket = null, string? memoria = null, EstadoProducto estado = EstadoProducto.Activo, bool destacado = false) => new()
    {
        Sku = sku, Nombre = nombre, Slug = sku.ToLowerInvariant(), Categoria = c, Marca = m, Precio = precio, PrecioOferta = oferta,
        PrecioCosto = precio / 2, Stock = stock, Estado = estado, Destacado = destacado,
        DescripcionCorta = "Descripción de " + nombre, Descripcion = "Larga",
        Compatibilidad = socket is null ? null : new ProductoCompatibilidad { Socket = socket, TiposMemoria = memoria }
    };

    private (ServicioCatalogo, AppDbContext) Servicio()
    {
        var db = _bd.NuevoContexto();
        return (new ServicioCatalogo(db, new ServicioPrecios(db, new RelojFijo(Hoy))), db);
    }

    private async Task<ResultadoCatalogo> Buscar(FiltroCatalogo f)
    {
        var (s, db) = Servicio();
        await using (db) return (await s.BuscarAsync(f))!;
    }

    private static string[] Skus(ResultadoCatalogo r) => r.Productos.Select(p => p.Sku).ToArray();

    [Fact]
    public async Task Lista_solo_productos_activos()
    {
        var r = await Buscar(new FiltroCatalogo());

        Assert.Equal(4, r.Total);
        Assert.DoesNotContain("CPU-OLD", Skus(r));
        Assert.DoesNotContain("CPU-RETRO", Skus(r));
    }

    [Fact]
    public async Task Filtra_por_categoria_y_devuelve_null_si_no_existe()
    {
        Assert.Equal(3, (await Buscar(new FiltroCatalogo { Categoria = "procesadores" })).Total);
        var (s, db) = Servicio();
        await using (db) Assert.Null(await s.BuscarAsync(new FiltroCatalogo { Categoria = "no-existe" }));
    }

    [Theory]
    [InlineData("ryzen", 2)]
    [InlineData("RYZEN 7800", 1)]       // todas las palabras deben coincidir, sin distinguir mayúsculas
    [InlineData("ryzen 7", 2)]          // "7" también aparece en "7600"
    [InlineData("intel", 1)]            // por marca (el inactivo no cuenta)
    [InlineData("mou-g502", 1)]         // por SKU
    [InlineData("procesadores", 3)]     // por categoría
    [InlineData("%", 0)]                // los comodines de LIKE se escapan
    [InlineData("_", 0)]
    [InlineData("nvidia", 0)]
    public async Task Busqueda_por_texto(string q, int esperado) =>
        Assert.Equal(esperado, (await Buscar(new FiltroCatalogo { Q = q })).Total);

    [Fact]
    public async Task Varias_marcas_se_combinan_con_O()
    {
        var r = await Buscar(new FiltroCatalogo { Categoria = "procesadores", Marca = ["amd", "intel"] });
        Assert.Equal(3, r.Total);

        var soloIntel = await Buscar(new FiltroCatalogo { Categoria = "procesadores", Marca = ["intel"] });
        Assert.Equal(["CPU-I5"], Skus(soloIntel));
    }

    [Fact]
    public async Task Rango_de_precio_usa_el_precio_final_con_ofertas_y_promociones()
    {
        // R5 cuesta 900 por oferta y G502 cuesta 300 por la promo de 25%. La promo vencida de 50% no aplica.
        var r = await Buscar(new FiltroCatalogo { Max = 950, Orden = OrdenCatalogo.PrecioAsc });

        Assert.Equal(["MOU-G502", "CPU-R5"], Skus(r));
        Assert.Equal(300m, r.Productos[0].Precio.PrecioFinal);
        Assert.Equal(900m, r.Productos[1].Precio.PrecioFinal);
    }

    [Fact]
    public async Task Filtros_de_disponibilidad_y_oferta_se_combinan()
    {
        Assert.DoesNotContain("CPU-R7", Skus(await Buscar(new FiltroCatalogo { Disponible = true })));

        var ofertas = await Buscar(new FiltroCatalogo { Oferta = true, Orden = OrdenCatalogo.Nombre });
        Assert.Equal(["CPU-R5", "MOU-G502"], Skus(ofertas));

        var ambos = await Buscar(new FiltroCatalogo { Oferta = true, Disponible = true, Categoria = "procesadores" });
        Assert.Equal(["CPU-R5"], Skus(ambos));
    }

    [Fact]
    public async Task Filtra_por_socket_y_tipo_de_memoria()
    {
        var am5 = await Buscar(new FiltroCatalogo { Categoria = "procesadores", Socket = "am5", Orden = OrdenCatalogo.Nombre });
        Assert.Equal(["CPU-R5", "CPU-R7"], Skus(am5));

        var ddr4 = await Buscar(new FiltroCatalogo { Categoria = "procesadores", Memoria = "DDR4" });
        Assert.Equal(["CPU-I5"], Skus(ddr4)); // el I5 admite DDR4 y DDR5
    }

    [Fact]
    public async Task Ordena_por_precio_final()
    {
        var asc = await Buscar(new FiltroCatalogo { Orden = OrdenCatalogo.PrecioAsc });
        Assert.Equal(["MOU-G502", "CPU-R5", "CPU-I5", "CPU-R7"], Skus(asc));

        var desc = await Buscar(new FiltroCatalogo { Orden = OrdenCatalogo.PrecioDesc });
        Assert.Equal(["CPU-R7", "CPU-I5", "CPU-R5", "MOU-G502"], Skus(desc));
    }

    [Fact]
    public async Task Relevancia_pone_agotados_al_final()
    {
        var r = await Buscar(new FiltroCatalogo { Categoria = "procesadores" });
        Assert.Equal("CPU-R7", Skus(r).Last());
        Assert.Equal("CPU-R5", Skus(r).First()); // destacado y con stock
    }

    [Fact]
    public async Task Facetas_cuentan_productos_por_marca_socket_y_memoria_en_la_categoria()
    {
        var r = await Buscar(new FiltroCatalogo { Categoria = "procesadores", Marca = ["amd"] });

        Assert.Equal([("amd", 2, true), ("intel", 1, false)], r.Marcas.Select(m => (m.Valor, m.Cantidad, m.Seleccionada)).ToArray());
        Assert.Equal([("AM5", 2), ("LGA1700", 1)], r.Sockets.Select(s => (s.Valor, s.Cantidad)).ToArray());
        Assert.Equal([("DDR4", 1), ("DDR5", 3)], r.Memorias.Select(s => (s.Valor, s.Cantidad)).ToArray());
        Assert.Equal(900m, r.PrecioMinimo);
        Assert.Equal(2000m, r.PrecioMaximo);
    }

    [Fact]
    public async Task Paginacion_corta_en_12_y_ajusta_paginas_fuera_de_rango()
    {
        await using (var db = _bd.NuevoContexto())
        {
            var c = db.Categorias.Single(x => x.Slug == "ratones");
            var m = db.Marcas.Single(x => x.Slug == "logitech");
            for (var i = 1; i <= 26; i++) db.Productos.Add(P($"MOU-{i:00}", $"Ratón {i:00}", c, m, 100 + i, null, 1));
            await db.SaveChangesAsync();
        }

        var p3 = await Buscar(new FiltroCatalogo { Categoria = "ratones", Pagina = 3 });
        Assert.Equal(27, p3.Total);
        Assert.Equal(3, p3.TotalPaginas);
        Assert.Equal(3, p3.Productos.Count);
        Assert.Equal((25, 27), (p3.Desde, p3.Hasta));

        var fuera = await Buscar(new FiltroCatalogo { Categoria = "ratones", Pagina = 99 });
        Assert.Equal(3, fuera.Filtro.Pagina);
        Assert.Equal(3, fuera.Productos.Count);
    }

    [Fact]
    public async Task Ficha_oculta_inactivos_y_muestra_descontinuados_sin_permitir_compra()
    {
        var (s, db) = Servicio();
        await using (db)
        {
            Assert.Null(await s.ObtenerFichaAsync("cpu-old"));

            var retro = await s.ObtenerFichaAsync("cpu-retro");
            Assert.NotNull(retro);
            Assert.False(retro.Comprable);

            var r5 = await s.ObtenerFichaAsync("cpu-r5");
            Assert.NotNull(r5);
            Assert.True(r5.Comprable);
            Assert.Equal(900m, r5.Precio.PrecioFinal);
        }
    }

    [Fact]
    public async Task Relacionados_excluyen_el_propio_e_inactivos_y_priorizan_mismo_socket()
    {
        var (s, db) = Servicio();
        await using (db)
        {
            var ficha = await s.ObtenerFichaAsync("cpu-i5");
            var skus = ficha!.Relacionados.Select(r => r.Sku).ToList();

            Assert.DoesNotContain("CPU-I5", skus);
            Assert.DoesNotContain("CPU-OLD", skus);
            Assert.DoesNotContain("CPU-RETRO", skus);
            Assert.Contains("CPU-R5", skus);

            var fichaR7 = await s.ObtenerFichaAsync("cpu-r7");
            Assert.Equal("CPU-R5", fichaR7!.Relacionados[0].Sku); // mismo socket AM5 y misma marca
        }
    }

    [Fact]
    public async Task Relacionados_de_un_periferico_se_completan_con_perifericos()
    {
        await using (var db = _bd.NuevoContexto())
        {
            var teclados = new Categoria { Nombre = "Teclados", Slug = "teclados", Orden = 3 };
            var m = db.Marcas.Single(x => x.Slug == "logitech");
            db.Productos.Add(P("KB-1", "Teclado G213", teclados, m, 300, null, 4));
            await db.SaveChangesAsync();
        }

        var (s, ctx) = Servicio();
        await using (ctx)
        {
            var ficha = await s.ObtenerFichaAsync("mou-g502");
            // No hay otros ratones: el primer relleno debe ser el teclado, no un procesador.
            Assert.Equal("KB-1", ficha!.Relacionados[0].Sku);
        }
    }
}
