using LlamitasStore.Data;
using LlamitasStore.Models.Entities;

namespace LlamitasStore.Tests.PcBuilder;

/// <summary>
/// Catálogo mínimo para el PC Builder:
///   CPU AM5 (65 W, con disipador y gráficos) · CPU LGA1700 (sin disipador ni gráficos)
///   Placa AM5 DDR5 ATX · Placa LGA1700 DDR5 ATX · RAM DDR5 32 GB (2 mód., stock 3) · RAM DDR4
///   GPU 220 W 300 mm · SSD M.2 · Fuente 750 W · Fuente 400 W · Gabinete ATX · Disipador AM5 (agotado) · Teclado (no es componente)
/// </summary>
public sealed class EscenarioPc
{
    public int CpuAm5, CpuIntel, PlacaAm5, PlacaIntel, RamDdr5, RamDdr4, Gpu, Ssd, Fuente750, Fuente400, Gabinete, Cooler, Teclado;

    public static async Task<EscenarioPc> CrearAsync(AppDbContext db)
    {
        var e = new EscenarioPc();
        var marca = new Marca { Nombre = "Marca PC", Slug = "marca-pc" };
        var cats = new Dictionary<TipoComponente, Categoria>();
        Categoria Cat(TipoComponente t) => cats.TryGetValue(t, out var c) ? c
            : cats[t] = new Categoria { Nombre = "Cat " + t, Slug = "cat-" + t.ToString().ToLowerInvariant(), TipoComponente = t };

        Producto P(string sku, TipoComponente tipo, decimal precio, ProductoCompatibilidad? comp, int stock = 10, bool destacado = false) => new()
        {
            Sku = sku, Nombre = "Pieza " + sku, Slug = "pieza-" + sku.ToLowerInvariant(), DescripcionCorta = "c", Descripcion = "d",
            Categoria = Cat(tipo), Marca = marca, Precio = precio, PrecioCosto = 1, Stock = stock, Destacado = destacado, Compatibilidad = comp
        };

        var productos = new[]
        {
            P("CPU-AM5", TipoComponente.Procesador, 1500, new() { Socket = "AM5", TiposMemoria = "DDR5", ConsumoW = 65, IncluyeCooler = true, GraficosIntegrados = true }, destacado: true),
            P("CPU-INTEL", TipoComponente.Procesador, 1800, new() { Socket = "LGA1700", TiposMemoria = "DDR4,DDR5", ConsumoW = 125, IncluyeCooler = false, GraficosIntegrados = false }),
            P("PLACA-AM5", TipoComponente.PlacaMadre, 1200, new() { Socket = "AM5", TiposMemoria = "DDR5", FactorForma = "ATX", SlotsMemoria = 4, MemoriaMaximaGb = 128, SlotsM2 = 2, PuertosSata = 4 }),
            P("PLACA-INTEL", TipoComponente.PlacaMadre, 1100, new() { Socket = "LGA1700", TiposMemoria = "DDR5", FactorForma = "ATX", SlotsMemoria = 4, MemoriaMaximaGb = 128, SlotsM2 = 2, PuertosSata = 4 }),
            P("RAM-DDR5", TipoComponente.MemoriaRam, 600, new() { TiposMemoria = "DDR5", CapacidadGb = 32, Modulos = 2 }, stock: 3),
            P("RAM-DDR4", TipoComponente.MemoriaRam, 400, new() { TiposMemoria = "DDR4", CapacidadGb = 16, Modulos = 2 }),
            P("GPU", TipoComponente.TarjetaGrafica, 4000, new() { ConsumoW = 220, LargoMm = 300, FuenteRecomendadaW = 650 }),
            P("SSD", TipoComponente.Almacenamiento, 500, new() { Interfaz = "M.2" }),
            P("PSU-750", TipoComponente.FuentePoder, 700, new() { PotenciaW = 750 }),
            P("PSU-400", TipoComponente.FuentePoder, 300, new() { PotenciaW = 400 }),
            P("GAB", TipoComponente.Gabinete, 600, new() { FactoresFormaSoportados = "ATX,Micro-ATX", LargoGpuMaximoMm = 365, AlturaCoolerMaximaMm = 165 }),
            P("COOLER", TipoComponente.Refrigeracion, 250, new() { AlturaMm = 155, SocketsSoportados = "AM5" }, stock: 0),
            P("TECLADO", TipoComponente.Ninguno, 300, null)
        };
        db.Productos.AddRange(productos);
        await db.SaveChangesAsync();

        (e.CpuAm5, e.CpuIntel, e.PlacaAm5, e.PlacaIntel, e.RamDdr5, e.RamDdr4, e.Gpu) =
            (productos[0].Id, productos[1].Id, productos[2].Id, productos[3].Id, productos[4].Id, productos[5].Id, productos[6].Id);
        (e.Ssd, e.Fuente750, e.Fuente400, e.Gabinete, e.Cooler, e.Teclado) =
            (productos[7].Id, productos[8].Id, productos[9].Id, productos[10].Id, productos[11].Id, productos[12].Id);
        return e;
    }

    /// <summary>Armado AM5 completo y compatible (URL del builder).</summary>
    public string UrlCompleta =>
        $"/pc-builder?cpu={CpuAm5}&placa={PlacaAm5}&ram={RamDdr5}&gpu={Gpu}&alm={Ssd}&psu={Fuente750}&gab={Gabinete}";
}
