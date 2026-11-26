using LlamitasStore.Models.Entities;
using LlamitasStore.Services.PcBuilder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace LlamitasStore.Tests.PcBuilder;

/// <summary>Cada regla se prueba en sus tres salidas: correcto, error (o advertencia) y sin dato.</summary>
public class ReglasCompatibilidadTests
{
    // Armado base compatible: AM5 + DDR5 + ATX. Consumo = 65 + 220 + 75 = 360 W → sugerida ceil(468/50)*50 = 500 W.
    private static Componente Cpu(string? socket = "AM5", string? mem = "DDR5", int? w = 65, bool? cooler = true, bool? igpu = true) =>
        new(1, "CPU", TipoComponente.Procesador, 1, Socket: socket, TiposMemoria: mem, ConsumoW: w, IncluyeCooler: cooler, GraficosIntegrados: igpu);
    private static Componente Placa(string? socket = "AM5", string? mem = "DDR5", string? forma = "ATX", int? slots = 4, int? max = 128, int? m2 = 2, int? sata = 4) =>
        new(2, "Placa", TipoComponente.PlacaMadre, 1, Socket: socket, TiposMemoria: mem, FactorForma: forma, SlotsMemoria: slots, MemoriaMaximaGb: max, SlotsM2: m2, PuertosSata: sata);
    private static Componente Ram(string? mem = "DDR5", int? gb = 32, int? modulos = 2, int cant = 1, int id = 3) =>
        new(id, "RAM " + id, TipoComponente.MemoriaRam, cant, TiposMemoria: mem, CapacidadGb: gb, Modulos: modulos);
    private static Componente Gpu(int? w = 220, int? largo = 300, int? recomendada = 650) =>
        new(4, "GPU", TipoComponente.TarjetaGrafica, 1, ConsumoW: w, LargoMm: largo, FuenteRecomendadaW: recomendada);
    private static Componente Disco(string? interfaz = "M.2", int id = 5) => new(id, "Disco " + id, TipoComponente.Almacenamiento, 1, Interfaz: interfaz);
    private static Componente Fuente(int? w = 750) => new(6, "PSU", TipoComponente.FuentePoder, 1, PotenciaW: w);
    private static Componente Gabinete(string? formas = "ATX,Micro-ATX", int? gpuMax = 365, int? coolerMax = 165) =>
        new(7, "Gabinete", TipoComponente.Gabinete, 1, FactoresFormaSoportados: formas, LargoGpuMaximoMm: gpuMax, AlturaCoolerMaximaMm: coolerMax);
    private static Componente Cooler(int? alto = 155, string? sockets = "AM4,AM5") =>
        new(8, "Cooler", TipoComponente.Refrigeracion, 1, AlturaMm: alto, SocketsSoportados: sockets);

    private static List<Componente> Completo() => [Cpu(), Placa(), Ram(), Gpu(), Disco(), Fuente(), Gabinete(), Cooler()];

    private static List<Componente> Con(params Componente[] reemplazos) =>
        Completo().Where(c => reemplazos.All(r => r.Tipo != c.Tipo)).Concat(reemplazos).ToList();

    private static Hallazgo Regla(EvaluacionPc ev, string regla) => Assert.Single(ev.Hallazgos, h => h.Regla == regla);

    [Fact]
    public void Armado_completo_compatible_no_tiene_errores_ni_faltantes()
    {
        var ev = ReglasCompatibilidad.Evaluar(Completo());

        Assert.False(ev.TieneErrores);
        Assert.False(ev.TieneAdvertencias);
        Assert.True(ev.Completa);
        Assert.All(ev.Hallazgos, h => Assert.Equal(Severidad.Correcto, h.Severidad));
        Assert.Equal(360, ev.ConsumoEstimadoW);
        Assert.Equal(500, ev.FuenteSugeridaW);
    }

    [Fact]
    public void Socket_distinto_es_error_y_lo_explica()
    {
        var h = Regla(ReglasCompatibilidad.Evaluar(Con(Cpu(socket: "LGA1700"))), "socket");
        Assert.Equal(Severidad.Error, h.Severidad);
        Assert.Contains("LGA1700", h.Mensaje);
        Assert.Contains("AM5", h.Mensaje);
        Assert.Equal([TipoComponente.Procesador, TipoComponente.PlacaMadre], h.Tipos);
    }

    [Fact]
    public void Memoria_de_otro_tipo_que_la_placa_es_error()
    {
        var ev = ReglasCompatibilidad.Evaluar(Con(Ram(mem: "DDR4")));
        Assert.Equal(Severidad.Error, Regla(ev, "memoria-ram").Severidad);
    }

    [Fact]
    public void Procesador_que_no_admite_la_memoria_de_la_placa_es_error()
    {
        var ev = ReglasCompatibilidad.Evaluar(Con(Cpu(mem: "DDR4"), Placa(mem: "DDR5")));
        Assert.Equal(Severidad.Error, Regla(ev, "memoria-cpu").Severidad);

        // Un procesador que admite ambos tipos es válido con una placa DDR4.
        var ok = ReglasCompatibilidad.Evaluar(Con(Cpu(mem: "DDR4,DDR5"), Placa(mem: "DDR4"), Ram(mem: "DDR4")));
        Assert.Equal(Severidad.Correcto, Regla(ok, "memoria-cpu").Severidad);
    }

    [Fact]
    public void Mas_modulos_que_ranuras_es_error()
    {
        // 3 kits de 2 módulos = 6 módulos en 4 ranuras.
        var ev = ReglasCompatibilidad.Evaluar(Con(Ram(cant: 3)));
        var h = Regla(ev, "ranuras-ram");
        Assert.Equal(Severidad.Error, h.Severidad);
        Assert.Contains("6 módulos", h.Mensaje);

        Assert.Equal(Severidad.Correcto, Regla(ReglasCompatibilidad.Evaluar(Con(Ram(cant: 2))), "ranuras-ram").Severidad);
    }

    [Fact]
    public void Superar_la_memoria_maxima_de_la_placa_es_error()
    {
        var ev = ReglasCompatibilidad.Evaluar(Con(Ram(gb: 96, modulos: 2, cant: 2))); // 192 GB > 128 GB
        Assert.Equal(Severidad.Error, Regla(ev, "capacidad-ram").Severidad);
    }

    [Fact]
    public void Mas_unidades_M2_que_ranuras_es_error_y_SATA_se_cuenta_aparte()
    {
        var discos = new[] { Disco("M.2", 50), Disco("M.2", 51), Disco("M.2", 52), Disco("SATA", 53) };
        var ev = ReglasCompatibilidad.Evaluar(Completo().Where(c => c.Tipo != TipoComponente.Almacenamiento).Concat(discos).ToList());

        Assert.Equal(Severidad.Error, Regla(ev, "ranuras-m2").Severidad);
        Assert.Equal(Severidad.Correcto, Regla(ev, "puertos-sata").Severidad);
    }

    [Fact]
    public void Placa_que_no_entra_en_el_gabinete_es_error()
    {
        var ev = ReglasCompatibilidad.Evaluar(Con(Gabinete(formas: "Mini-ITX")));
        Assert.Equal(Severidad.Error, Regla(ev, "formato").Severidad);
    }

    [Fact]
    public void Grafica_mas_larga_que_el_gabinete_es_error()
    {
        var h = Regla(ReglasCompatibilidad.Evaluar(Con(Gpu(largo: 380))), "largo-gpu");
        Assert.Equal(Severidad.Error, h.Severidad);
        Assert.Contains("380 mm", h.Mensaje);
        Assert.Contains("365 mm", h.Mensaje);
    }

    [Fact]
    public void Disipador_alto_o_sin_montaje_para_el_socket_es_error()
    {
        Assert.Equal(Severidad.Error, Regla(ReglasCompatibilidad.Evaluar(Con(Cooler(alto: 170))), "altura-cooler").Severidad);
        Assert.Equal(Severidad.Error, Regla(ReglasCompatibilidad.Evaluar(Con(Cooler(sockets: "LGA1700"))), "socket-cooler").Severidad);
    }

    [Fact]
    public void Procesador_sin_disipador_incluido_exige_refrigeracion()
    {
        var sinCooler = Completo().Where(c => c.Tipo != TipoComponente.Refrigeracion).Select(c => c.Tipo == TipoComponente.Procesador ? Cpu(cooler: false) : c).ToList();
        var ev = ReglasCompatibilidad.Evaluar(sinCooler);

        Assert.Equal(Severidad.Error, Regla(ev, "refrigeracion").Severidad);
        Assert.Contains(TipoComponente.Refrigeracion, ev.Faltantes);

        // Con disipador incluido, la refrigeración es opcional.
        var conIncluido = ReglasCompatibilidad.Evaluar(Completo().Where(c => c.Tipo != TipoComponente.Refrigeracion).ToList());
        Assert.Equal(Severidad.Correcto, Regla(conIncluido, "refrigeracion").Severidad);
        Assert.True(conIncluido.Completa);
    }

    [Fact]
    public void Sin_grafica_y_sin_graficos_integrados_no_hay_video()
    {
        var lista = Completo().Where(c => c.Tipo != TipoComponente.TarjetaGrafica).Select(c => c.Tipo == TipoComponente.Procesador ? Cpu(igpu: false) : c).ToList();
        var ev = ReglasCompatibilidad.Evaluar(lista);

        Assert.Equal(Severidad.Error, Regla(ev, "video").Severidad);
        Assert.Contains(TipoComponente.TarjetaGrafica, ev.Faltantes);

        var integrados = ReglasCompatibilidad.Evaluar(Completo().Where(c => c.Tipo != TipoComponente.TarjetaGrafica).ToList());
        Assert.Equal(Severidad.Correcto, Regla(integrados, "video").Severidad);
    }

    [Theory]
    [InlineData(350, Severidad.Error)]       // < 360 W de consumo
    [InlineData(450, Severidad.Advertencia)] // cubre, pero < 500 W sugeridos
    [InlineData(750, Severidad.Correcto)]
    public void Fuente_se_evalua_contra_consumo_y_margen(int potencia, Severidad esperada)
    {
        var ev = ReglasCompatibilidad.Evaluar(Con(Fuente(potencia)));
        Assert.Equal(esperada, Regla(ev, "fuente").Severidad);
    }

    [Fact]
    public void Fuente_por_debajo_de_la_recomendacion_de_la_grafica_es_advertencia()
    {
        var ev = ReglasCompatibilidad.Evaluar(Con(Fuente(600))); // ≥ 500 sugeridos, pero la GPU pide 650
        Assert.Equal(Severidad.Correcto, Regla(ev, "fuente").Severidad);
        Assert.Equal(Severidad.Advertencia, Regla(ev, "fuente-gpu").Severidad);
        Assert.False(ev.TieneErrores);
    }

    [Fact]
    public void Dato_faltante_queda_sin_verificar_nunca_como_compatible()
    {
        var ev = ReglasCompatibilidad.Evaluar(Con(Placa(socket: null), Gpu(largo: null), Fuente(null)));

        Assert.Equal(Severidad.SinVerificar, Regla(ev, "socket").Severidad);
        Assert.Equal(Severidad.SinVerificar, Regla(ev, "largo-gpu").Severidad);
        Assert.Equal(Severidad.SinVerificar, Regla(ev, "fuente").Severidad);
        Assert.Contains("No se pudo verificar", Regla(ev, "socket").Mensaje);
    }

    [Fact]
    public void Los_hallazgos_mas_graves_van_primero_y_se_marcan_por_componente()
    {
        var ev = ReglasCompatibilidad.Evaluar(Con(Cpu(socket: "AM4"), Fuente(450)));

        Assert.Equal(Severidad.Error, ev.Hallazgos[0].Severidad);
        Assert.Equal(Severidad.Error, ev.PeorPara(TipoComponente.Procesador));
        Assert.Equal(Severidad.Advertencia, ev.PeorPara(TipoComponente.FuentePoder));
        Assert.Equal(Severidad.Correcto, ev.PeorPara(TipoComponente.Gabinete));
    }

    [Fact]
    public void Armado_vacio_lista_todos_los_obligatorios_como_faltantes()
    {
        var ev = ReglasCompatibilidad.Evaluar([]);
        Assert.Empty(ev.Hallazgos);
        Assert.Equal(ReglasCompatibilidad.Obligatorios, ev.Faltantes);
        Assert.Equal(ReglasCompatibilidad.ConsumoBaseW, ev.ConsumoEstimadoW);
    }
}

public class SeleccionPcTests
{
    private static IQueryCollection Q(params (string K, string V)[] pares) =>
        new QueryCollection(pares.ToDictionary(p => p.K, p => new StringValues(p.V)));

    [Fact]
    public void Lee_ids_cantidades_y_multiples_de_la_url()
    {
        var s = SeleccionPc.Desde(Q(("cpu", "3"), ("ram", "12*2"), ("alm", "20,21"), ("config", "7")));

        Assert.Equal([(3, 1)], s.De(TipoComponente.Procesador));
        Assert.Equal([(12, 2)], s.De(TipoComponente.MemoriaRam));
        Assert.Equal([(20, 1), (21, 1)], s.De(TipoComponente.Almacenamiento));
        Assert.Equal(7, s.ConfiguracionId);
    }

    [Fact]
    public void Descarta_valores_invalidos_y_limita_cantidades()
    {
        var s = SeleccionPc.Desde(Q(("cpu", "abc"), ("placa", "-4"), ("ram", "12*99"), ("gpu", "5,6"), ("alm", "1,1,2,3,4,5,6"), ("config", "0")));

        Assert.Empty(s.De(TipoComponente.Procesador));
        Assert.Empty(s.De(TipoComponente.PlacaMadre));
        Assert.Equal([(12, 4)], s.De(TipoComponente.MemoriaRam));          // máximo 4 kits
        Assert.Equal([(5, 1)], s.De(TipoComponente.TarjetaGrafica));        // paso único: solo el primero
        Assert.Equal(SeleccionPc.MaxUnidadesAlmacenamiento, s.De(TipoComponente.Almacenamiento).Count);
        Assert.Equal(4, s.De(TipoComponente.Almacenamiento).Select(x => x.Id).Distinct().Count()); // sin repetidos
        Assert.Null(s.ConfiguracionId);
    }

    [Fact]
    public void Query_y_campos_reconstruyen_la_misma_seleccion()
    {
        var original = SeleccionPc.Desde(Q(("cpu", "3"), ("ram", "12*2"), ("alm", "20,21"), ("config", "7")));
        var url = original.Query("ram", todos: true);

        Assert.Equal("/pc-builder?cpu=3&ram=12%2A2&alm=20%2C21&config=7&paso=ram&todos=true", url);

        var desdeUrl = SeleccionPc.Desde(new QueryCollection(Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(url[url.IndexOf('?')..])));
        var desdeCampos = SeleccionPc.Desde(new QueryCollection(original.Campos().ToDictionary(c => c.Clave, c => new StringValues(c.Valor))));
        foreach (var copia in new[] { desdeUrl, desdeCampos })
        {
            Assert.Equal(original.Todos, copia.Todos);
            Assert.Equal(7, copia.ConfiguracionId);
        }
    }

    [Fact]
    public void Poner_reemplaza_en_pasos_unicos_y_suma_en_almacenamiento_sin_tocar_el_original()
    {
        var s = new SeleccionPc().Poner(TipoComponente.Procesador, 1).Poner(TipoComponente.Almacenamiento, 10, agregar: true);
        var copia = s.Clonar().Poner(TipoComponente.Procesador, 2).Poner(TipoComponente.Almacenamiento, 11, agregar: true);

        Assert.Equal([(1, 1)], s.De(TipoComponente.Procesador));
        Assert.Single(s.De(TipoComponente.Almacenamiento));
        Assert.Equal([(2, 1)], copia.De(TipoComponente.Procesador));
        Assert.Equal(2, copia.De(TipoComponente.Almacenamiento).Count);

        copia.Quitar(TipoComponente.Almacenamiento, 10);
        Assert.Equal([(11, 1)], copia.De(TipoComponente.Almacenamiento));
    }
}
