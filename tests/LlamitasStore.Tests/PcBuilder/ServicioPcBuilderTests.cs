using LlamitasStore.Models.Entities;
using LlamitasStore.Services;
using LlamitasStore.Services.Catalogo;
using LlamitasStore.Services.PcBuilder;
using LlamitasStore.Services.Ventas;
using LlamitasStore.Tests.Soporte;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace LlamitasStore.Tests.PcBuilder;

public sealed class ServicioPcBuilderTests : IAsyncLifetime
{
    private static readonly DateTime Hoy = new(2026, 11, 26, 12, 0, 0, DateTimeKind.Utc);
    private BaseDatosPrueba _bd = null!;
    private EscenarioPc _e = null!;
    private int _ana, _beto;

    public async Task InitializeAsync()
    {
        _bd = await BaseDatosPrueba.CrearAsync();
        await using var db = _bd.NuevoContexto();
        _e = await EscenarioPc.CrearAsync(db);
        var ana = new Usuario { UserName = "ana@x.bo", Email = "ana@x.bo", Nombre = "Ana", Apellido = "A", SecurityStamp = "s1" };
        var beto = new Usuario { UserName = "beto@x.bo", Email = "beto@x.bo", Nombre = "Beto", Apellido = "B", SecurityStamp = "s2" };
        db.Users.AddRange(ana, beto);
        await db.SaveChangesAsync();
        (_ana, _beto) = (ana.Id, beto.Id);
    }

    public async Task DisposeAsync() => await _bd.DisposeAsync();

    private (ServicioPcBuilder Builder, ServicioCarrito Carrito) Servicios()
    {
        var db = _bd.NuevoContexto();
        var reloj = new RelojFijo(Hoy);
        var precios = new ServicioPrecios(db, reloj);
        var carrito = new ServicioCarrito(db, precios, new ServicioCupones(db, reloj), new AjustesTienda(db, new MemoryCache(new MemoryCacheOptions())));
        return (new ServicioPcBuilder(db, precios, carrito, reloj), carrito);
    }

    private static SeleccionPc Sel(params (TipoComponente T, int Id, int Cant)[] items)
    {
        var s = new SeleccionPc();
        foreach (var (t, id, c) in items) s.Poner(t, id, c, agregar: SeleccionPc.Paso(t).Multiple);
        return s;
    }

    private SeleccionPc Completa() => Sel(
        (TipoComponente.Procesador, _e.CpuAm5, 1), (TipoComponente.PlacaMadre, _e.PlacaAm5, 1), (TipoComponente.MemoriaRam, _e.RamDdr5, 2),
        (TipoComponente.TarjetaGrafica, _e.Gpu, 1), (TipoComponente.Almacenamiento, _e.Ssd, 1), (TipoComponente.FuentePoder, _e.Fuente750, 1),
        (TipoComponente.Gabinete, _e.Gabinete, 1));

    [Fact]
    public async Task Armado_completo_carga_datos_reales_y_suma_el_total()
    {
        var armado = await Servicios().Builder.ArmarAsync(Completa());

        Assert.Equal(7, armado.Componentes.Count);
        Assert.Empty(armado.Avisos);
        Assert.False(armado.Evaluacion.TieneErrores);
        Assert.True(armado.Evaluacion.Completa);
        Assert.Equal(1500 + 1200 + 600 * 2 + 4000 + 500 + 700 + 600, armado.Total);
        Assert.Equal(65 + 220 + ReglasCompatibilidad.ConsumoBaseW, armado.Evaluacion.ConsumoEstimadoW);
    }

    [Fact]
    public async Task Ids_inexistentes_o_de_otro_tipo_se_descartan_con_aviso()
    {
        // El teclado no es componente; la placa no puede ir en el paso de gráfica; 9999 no existe.
        var s = Sel((TipoComponente.Procesador, _e.CpuAm5, 1), (TipoComponente.TarjetaGrafica, _e.PlacaAm5, 1),
            (TipoComponente.Gabinete, 9999, 1), (TipoComponente.Almacenamiento, _e.Teclado, 1));
        var armado = await Servicios().Builder.ArmarAsync(s);

        Assert.Single(armado.Componentes);
        Assert.Equal(3, armado.Avisos.Count);
        Assert.Equal($"/pc-builder?cpu={_e.CpuAm5}", armado.Seleccion.Query());
    }

    [Fact]
    public async Task Opciones_marcan_incompatibles_con_motivo_y_sugieren_una_compatible()
    {
        var (builder, _) = Servicios();
        var armado = await builder.ArmarAsync(Sel((TipoComponente.Procesador, _e.CpuAm5, 1)));
        var opciones = await builder.OpcionesAsync(armado, SeleccionPc.Paso(TipoComponente.PlacaMadre));

        var intel = Assert.Single(opciones, o => o.Componente.Datos.ProductoId == _e.PlacaIntel);
        Assert.Equal(Severidad.Error, intel.Estado);
        Assert.Contains(intel.Motivos, m => m.Contains("LGA1700"));

        var am5 = Assert.Single(opciones, o => o.Componente.Datos.ProductoId == _e.PlacaAm5);
        Assert.Equal(Severidad.Correcto, am5.Estado);
        Assert.True(am5.Sugerida);
        Assert.Equal(opciones[0], am5); // compatibles primero
        // Elegir avanza al siguiente paso pendiente (la URL no fija "paso").
        Assert.Equal($"/pc-builder?cpu={_e.CpuAm5}&placa={_e.PlacaAm5}", am5.UrlElegir);
    }

    [Fact]
    public async Task Opciones_muestran_la_diferencia_de_precio_y_no_sugieren_agotados()
    {
        var (builder, _) = Servicios();
        var armado = await builder.ArmarAsync(Sel((TipoComponente.Procesador, _e.CpuAm5, 1), (TipoComponente.FuentePoder, _e.Fuente750, 1)));

        var fuentes = await builder.OpcionesAsync(armado, SeleccionPc.Paso(TipoComponente.FuentePoder));
        var elegida = Assert.Single(fuentes, o => o.Elegida);
        Assert.Equal(_e.Fuente750, elegida.Componente.Datos.ProductoId);
        Assert.Equal(-400m, Assert.Single(fuentes, o => o.Componente.Datos.ProductoId == _e.Fuente400).Diferencia);
        // Tocar la elegida la quita del armado.
        Assert.DoesNotContain($"psu={_e.Fuente750}", elegida.UrlElegir);

        var coolers = await builder.OpcionesAsync(armado, SeleccionPc.Paso(TipoComponente.Refrigeracion));
        var cooler = Assert.Single(coolers);
        Assert.Equal(Severidad.Correcto, cooler.Estado);
        Assert.False(cooler.Sugerida); // sin stock
    }

    [Fact]
    public async Task Guardar_listar_abrir_y_eliminar_solo_las_propias()
    {
        var (builder, _) = Servicios();

        var (id, error) = await builder.GuardarAsync(_ana, Completa(), "  Gamer AM5  ");
        Assert.Null(error);

        var lista = await builder.ListarAsync(_ana);
        var resumen = Assert.Single(lista);
        Assert.Equal("Gamer AM5", resumen.Nombre);
        Assert.Equal(7, resumen.Componentes);
        Assert.Contains($"config={id}", resumen.Url);
        Assert.Contains($"ram={_e.RamDdr5}%2A2", resumen.Url); // la cantidad de kits se conserva

        Assert.Empty(await builder.ListarAsync(_beto));
        Assert.Null(await builder.AbrirAsync(_beto, id!.Value));
        Assert.False(await builder.EliminarAsync(_beto, id.Value));

        var abierta = await builder.AbrirAsync(_ana, id.Value);
        Assert.NotNull(abierta);
        Assert.Equal(Completa().Todos.OrderBy(x => x.Id), abierta.Value.Seleccion.Todos.OrderBy(x => x.Id));

        Assert.True(await builder.EliminarAsync(_ana, id.Value));
        Assert.Empty(await Servicios().Builder.ListarAsync(_ana));
    }

    [Fact]
    public async Task Guardar_con_el_id_propio_actualiza_y_con_uno_ajeno_crea_otra()
    {
        var (builder, _) = Servicios();
        var (id, _) = await builder.GuardarAsync(_ana, Completa(), "Primera");

        var cambio = Sel((TipoComponente.Procesador, _e.CpuIntel, 1));
        cambio.ConfiguracionId = id;
        var (id2, _) = await Servicios().Builder.GuardarAsync(_ana, cambio, "Renombrada");
        Assert.Equal(id, id2);

        var deBeto = Sel((TipoComponente.Procesador, _e.CpuAm5, 1));
        deBeto.ConfiguracionId = id;
        var (id3, _) = await Servicios().Builder.GuardarAsync(_beto, deBeto, "Intento");
        Assert.NotEqual(id, id3);

        await using var db = _bd.NuevoContexto();
        var deAna = await db.ConfiguracionesPc.Include(c => c.Items).SingleAsync(c => c.Id == id);
        Assert.Equal("Renombrada", deAna.Nombre);
        Assert.Equal(_e.CpuIntel, Assert.Single(deAna.Items).ProductoId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("x")]
    public async Task Guardar_valida_nombre_y_que_haya_componentes(string nombre)
    {
        var (builder, _) = Servicios();
        Assert.NotNull((await builder.GuardarAsync(_ana, Completa(), nombre)).Error);
        Assert.NotNull((await builder.GuardarAsync(_ana, new SeleccionPc(), "Vacía")).Error);
        Assert.NotNull((await builder.GuardarAsync(_ana, Completa(), new string('a', 101))).Error);
    }

    [Fact]
    public async Task Agregar_al_carrito_respeta_cantidades_y_stock()
    {
        var (builder, carrito) = Servicios();
        var dueno = new DuenoCarrito(_ana, null);

        var s = Completa();
        s.Poner(TipoComponente.MemoriaRam, _e.RamDdr5, 4); // pide 4 kits, hay 3 en stock
        var (agregados, problemas) = await builder.AgregarAlCarritoAsync(dueno, s);

        Assert.Equal(7, agregados);
        Assert.Contains("máximo disponible", Assert.Single(problemas));

        var vista = await carrito.ObtenerAsync(dueno);
        Assert.Equal(7, vista.Items.Count);
        Assert.Equal(3, vista.Items.Single(i => i.ProductoId == _e.RamDdr5).Cantidad);
    }
}
