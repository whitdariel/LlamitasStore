using LlamitasStore.Models.Entities;

namespace LlamitasStore.Services.PcBuilder;

public enum Severidad { Correcto = 0, SinVerificar = 1, Advertencia = 2, Error = 3 }

/// <summary>Resultado de una regla. <see cref="Tipos"/> indica qué componentes involucra (para marcarlos en la interfaz).</summary>
public record Hallazgo(string Regla, Severidad Severidad, string Mensaje, TipoComponente[] Tipos);

/// <summary>Datos técnicos de un componente elegido (copia de ProductoCompatibilidad + nombre y cantidad).</summary>
public record Componente(
    int ProductoId, string Nombre, TipoComponente Tipo, int Cantidad,
    string? Socket = null, string? TiposMemoria = null, string? FactorForma = null, string? FactoresFormaSoportados = null,
    int? SlotsMemoria = null, int? MemoriaMaximaGb = null, int? CapacidadGb = null, int? Modulos = null,
    int? ConsumoW = null, int? PotenciaW = null, int? FuenteRecomendadaW = null, int? LargoMm = null, int? LargoGpuMaximoMm = null,
    int? AlturaMm = null, int? AlturaCoolerMaximaMm = null, string? SocketsSoportados = null, string? Interfaz = null,
    int? SlotsM2 = null, int? PuertosSata = null, bool? GraficosIntegrados = null, bool? IncluyeCooler = null);

public record EvaluacionPc(IReadOnlyList<Hallazgo> Hallazgos, int ConsumoEstimadoW, int FuenteSugeridaW, IReadOnlyList<TipoComponente> Faltantes)
{
    public bool TieneErrores => Hallazgos.Any(h => h.Severidad == Severidad.Error);
    public bool TieneAdvertencias => Hallazgos.Any(h => h.Severidad == Severidad.Advertencia);
    public bool Completa => Faltantes.Count == 0;
    public Severidad PeorPara(TipoComponente t) =>
        Hallazgos.Where(h => h.Tipos.Contains(t)).Select(h => h.Severidad).DefaultIfEmpty(Severidad.Correcto).Max();
}

/// <summary>
/// Reglas técnicas verificables del PC Builder. Cada regla compara datos de las fichas (fabricante) y explica
/// el resultado con los valores concretos. Si falta un dato, la regla queda "Sin verificar": nunca se asume compatible.
/// </summary>
public static class ReglasCompatibilidad
{
    /// <summary>Componentes imprescindibles para que el equipo encienda. La refrigeración depende del procesador y la GPU de si tiene gráficos integrados.</summary>
    public static readonly TipoComponente[] Obligatorios =
        [TipoComponente.Procesador, TipoComponente.PlacaMadre, TipoComponente.MemoriaRam, TipoComponente.Almacenamiento, TipoComponente.FuentePoder, TipoComponente.Gabinete];

    /// <summary>Consumo fijo estimado del resto del equipo (placa, memorias, discos y ventiladores).</summary>
    public const int ConsumoBaseW = 75;
    /// <summary>Margen recomendado sobre el consumo estimado (picos de carga y eficiencia de la fuente).</summary>
    public const double MargenFuente = 1.3;

    public static EvaluacionPc Evaluar(IReadOnlyCollection<Componente> componentes)
    {
        var h = new List<Hallazgo>();
        Componente? Uno(TipoComponente t) => componentes.FirstOrDefault(c => c.Tipo == t);
        var cpu = Uno(TipoComponente.Procesador);
        var placa = Uno(TipoComponente.PlacaMadre);
        var gpu = Uno(TipoComponente.TarjetaGrafica);
        var fuente = Uno(TipoComponente.FuentePoder);
        var gabinete = Uno(TipoComponente.Gabinete);
        var cooler = Uno(TipoComponente.Refrigeracion);
        var rams = componentes.Where(c => c.Tipo == TipoComponente.MemoriaRam).ToList();
        var discos = componentes.Where(c => c.Tipo == TipoComponente.Almacenamiento).ToList();

        // 1. Socket procesador ↔ placa
        if (cpu is not null && placa is not null)
            h.Add(Comparar("socket", cpu.Socket, placa.Socket, [TipoComponente.Procesador, TipoComponente.PlacaMadre],
                ok: $"Socket {cpu.Socket}: el procesador y la placa madre encajan.",
                error: $"El procesador usa socket {cpu.Socket} y la placa madre socket {placa.Socket}: no encajan físicamente.",
                faltante: "socket"));

        // 2. Tipo de memoria: placa ↔ RAM, y procesador ↔ placa
        if (placa is not null)
        {
            foreach (var ram in rams)
                h.Add(Comparar("memoria-ram", ram.TiposMemoria, placa.TiposMemoria, [TipoComponente.MemoriaRam, TipoComponente.PlacaMadre],
                    ok: $"{ram.TiposMemoria}: la memoria es del tipo que admite la placa.",
                    error: $"La placa madre admite {placa.TiposMemoria} y la memoria «{ram.Nombre}» es {ram.TiposMemoria}: las ranuras no son compatibles.",
                    faltante: "tipo de memoria"));

            if (cpu is not null)
            {
                if (cpu.TiposMemoria is null || placa.TiposMemoria is null)
                    h.Add(SinDato("memoria-cpu", "tipo de memoria", TipoComponente.Procesador, TipoComponente.PlacaMadre));
                else if (!Lista(cpu.TiposMemoria).Contains(placa.TiposMemoria, StringComparer.OrdinalIgnoreCase))
                    h.Add(new("memoria-cpu", Severidad.Error,
                        $"El procesador admite {cpu.TiposMemoria.Replace(",", " / ")} y la placa usa {placa.TiposMemoria}.",
                        [TipoComponente.Procesador, TipoComponente.PlacaMadre]));
                else
                    h.Add(new("memoria-cpu", Severidad.Correcto, $"El procesador admite la memoria {placa.TiposMemoria} de la placa.",
                        [TipoComponente.Procesador, TipoComponente.PlacaMadre]));
            }

            // 3. Ranuras y capacidad máxima
            if (rams.Count > 0)
            {
                if (rams.Any(r => r.Modulos is null) || placa.SlotsMemoria is null)
                    h.Add(SinDato("ranuras-ram", "número de módulos o de ranuras", TipoComponente.MemoriaRam, TipoComponente.PlacaMadre));
                else
                {
                    var modulos = rams.Sum(r => r.Modulos!.Value * r.Cantidad);
                    h.Add(modulos > placa.SlotsMemoria
                        ? new("ranuras-ram", Severidad.Error, $"Elegiste {modulos} módulos de memoria y la placa tiene {placa.SlotsMemoria} ranuras.", [TipoComponente.MemoriaRam, TipoComponente.PlacaMadre])
                        : new("ranuras-ram", Severidad.Correcto, $"{modulos} de {placa.SlotsMemoria} ranuras de memoria ocupadas.", [TipoComponente.MemoriaRam, TipoComponente.PlacaMadre]));
                }

                if (rams.Any(r => r.CapacidadGb is null) || placa.MemoriaMaximaGb is null)
                    h.Add(SinDato("capacidad-ram", "capacidad de memoria", TipoComponente.MemoriaRam, TipoComponente.PlacaMadre));
                else
                {
                    var total = rams.Sum(r => r.CapacidadGb!.Value * r.Cantidad);
                    h.Add(total > placa.MemoriaMaximaGb
                        ? new("capacidad-ram", Severidad.Error, $"Suman {total} GB de memoria y la placa admite como máximo {placa.MemoriaMaximaGb} GB.", [TipoComponente.MemoriaRam, TipoComponente.PlacaMadre])
                        : new("capacidad-ram", Severidad.Correcto, $"{total} GB de memoria (la placa admite hasta {placa.MemoriaMaximaGb} GB).", [TipoComponente.MemoriaRam, TipoComponente.PlacaMadre]));
                }
            }

            // 4. Puertos de almacenamiento
            if (discos.Count > 0)
            {
                if (discos.Any(d => d.Interfaz is null))
                    h.Add(SinDato("almacenamiento", "interfaz de almacenamiento", TipoComponente.Almacenamiento));
                else
                {
                    var m2 = discos.Where(d => d.Interfaz!.Equals("M.2", StringComparison.OrdinalIgnoreCase)).Sum(d => d.Cantidad);
                    var sata = discos.Where(d => d.Interfaz!.Equals("SATA", StringComparison.OrdinalIgnoreCase)).Sum(d => d.Cantidad);
                    if (m2 > 0) h.Add(Capacidad("ranuras-m2", m2, placa.SlotsM2, "unidades M.2", "ranuras M.2"));
                    if (sata > 0) h.Add(Capacidad("puertos-sata", sata, placa.PuertosSata, "unidades SATA", "puertos SATA"));
                }
            }
        }

        // 5. Formato de placa ↔ gabinete
        if (placa is not null && gabinete is not null)
        {
            if (placa.FactorForma is null || gabinete.FactoresFormaSoportados is null)
                h.Add(SinDato("formato", "formato de placa", TipoComponente.PlacaMadre, TipoComponente.Gabinete));
            else if (!Lista(gabinete.FactoresFormaSoportados).Contains(placa.FactorForma, StringComparer.OrdinalIgnoreCase))
                h.Add(new("formato", Severidad.Error,
                    $"La placa es {placa.FactorForma} y el gabinete solo admite {gabinete.FactoresFormaSoportados.Replace(",", ", ")}.",
                    [TipoComponente.PlacaMadre, TipoComponente.Gabinete]));
            else
                h.Add(new("formato", Severidad.Correcto, $"La placa {placa.FactorForma} entra en el gabinete.", [TipoComponente.PlacaMadre, TipoComponente.Gabinete]));
        }

        // 6. Largo de la GPU ↔ gabinete
        if (gpu is not null && gabinete is not null)
            h.Add(Medida("largo-gpu", gpu.LargoMm, gabinete.LargoGpuMaximoMm, "La tarjeta gráfica mide", "el gabinete admite hasta",
                TipoComponente.TarjetaGrafica, TipoComponente.Gabinete));

        // 7. Disipador: altura ↔ gabinete y socket ↔ procesador
        if (cooler is not null)
        {
            if (gabinete is not null)
                h.Add(Medida("altura-cooler", cooler.AlturaMm, gabinete.AlturaCoolerMaximaMm, "El disipador mide", "el gabinete admite hasta",
                    TipoComponente.Refrigeracion, TipoComponente.Gabinete));
            if (cpu is not null)
            {
                if (cooler.SocketsSoportados is null || cpu.Socket is null)
                    h.Add(SinDato("socket-cooler", "socket del disipador", TipoComponente.Refrigeracion, TipoComponente.Procesador));
                else if (!Lista(cooler.SocketsSoportados).Contains(cpu.Socket, StringComparer.OrdinalIgnoreCase))
                    h.Add(new("socket-cooler", Severidad.Error, $"El disipador no tiene montaje para socket {cpu.Socket} (admite {cooler.SocketsSoportados.Replace(",", ", ")}).",
                        [TipoComponente.Refrigeracion, TipoComponente.Procesador]));
                else
                    h.Add(new("socket-cooler", Severidad.Correcto, $"El disipador tiene montaje para socket {cpu.Socket}.", [TipoComponente.Refrigeracion, TipoComponente.Procesador]));
            }
        }
        else if (cpu is not null)
        {
            if (cpu.IncluyeCooler == true)
                h.Add(new("refrigeracion", Severidad.Correcto, "El procesador incluye disipador en la caja.", [TipoComponente.Procesador]));
            else if (cpu.IncluyeCooler == false)
                h.Add(new("refrigeracion", Severidad.Error, "El procesador no incluye disipador: agrega uno en Refrigeración.", [TipoComponente.Procesador, TipoComponente.Refrigeracion]));
            else
                h.Add(SinDato("refrigeracion", "si incluye disipador", TipoComponente.Procesador));
        }

        // 8. Salida de video
        if (gpu is null && cpu is not null)
        {
            if (cpu.GraficosIntegrados == false)
                h.Add(new("video", Severidad.Error, "El procesador no tiene gráficos integrados: sin tarjeta gráfica el equipo no dará imagen.",
                    [TipoComponente.Procesador, TipoComponente.TarjetaGrafica]));
            else if (cpu.GraficosIntegrados == true)
                h.Add(new("video", Severidad.Correcto, "Sin tarjeta gráfica: se usarán los gráficos integrados del procesador.", [TipoComponente.Procesador]));
            else
                h.Add(SinDato("video", "si tiene gráficos integrados", TipoComponente.Procesador));
        }

        // 9. Fuente de poder: consumo estimado y recomendación del fabricante de la GPU
        var consumo = (cpu?.ConsumoW ?? 0) + (gpu?.ConsumoW ?? 0) + ConsumoBaseW;
        var sugerida = (int)(Math.Ceiling(consumo * MargenFuente / 50.0) * 50);
        if (fuente is not null)
        {
            var faltaDato = fuente.PotenciaW is null || (cpu is not null && cpu.ConsumoW is null) || (gpu is not null && gpu.ConsumoW is null);
            if (faltaDato)
                h.Add(SinDato("fuente", "consumo o potencia", TipoComponente.FuentePoder));
            else if (fuente.PotenciaW < consumo)
                h.Add(new("fuente", Severidad.Error, $"La fuente de {fuente.PotenciaW} W no alcanza el consumo estimado de {consumo} W.", [TipoComponente.FuentePoder]));
            else if (fuente.PotenciaW < sugerida)
                h.Add(new("fuente", Severidad.Advertencia,
                    $"La fuente de {fuente.PotenciaW} W cubre el consumo estimado de {consumo} W, pero con poco margen: se sugieren {sugerida} W.", [TipoComponente.FuentePoder]));
            else
                h.Add(new("fuente", Severidad.Correcto, $"Fuente de {fuente.PotenciaW} W para un consumo estimado de {consumo} W (sugerido ≥ {sugerida} W).", [TipoComponente.FuentePoder]));

            if (gpu?.FuenteRecomendadaW is { } recomendada && fuente.PotenciaW < recomendada)
                h.Add(new("fuente-gpu", Severidad.Advertencia,
                    $"El fabricante de la tarjeta gráfica recomienda una fuente de al menos {recomendada} W.", [TipoComponente.FuentePoder, TipoComponente.TarjetaGrafica]));
        }

        var faltantes = Obligatorios.Where(t => componentes.All(c => c.Tipo != t)).ToList();
        if (cpu is { IncluyeCooler: false } && cooler is null && !faltantes.Contains(TipoComponente.Refrigeracion)) faltantes.Add(TipoComponente.Refrigeracion);
        if (cpu is { GraficosIntegrados: false } && gpu is null) faltantes.Add(TipoComponente.TarjetaGrafica);

        return new EvaluacionPc(h.OrderByDescending(x => x.Severidad).ToList(), consumo, sugerida, faltantes);
    }

    private static Hallazgo Comparar(string regla, string? a, string? b, TipoComponente[] tipos, string ok, string error, string faltante) =>
        a is null || b is null
            ? new(regla, Severidad.SinVerificar, $"No se pudo verificar: falta el dato «{faltante}» en alguna ficha.", tipos)
            : string.Equals(a, b, StringComparison.OrdinalIgnoreCase) ? new(regla, Severidad.Correcto, ok, tipos) : new(regla, Severidad.Error, error, tipos);

    private static Hallazgo Medida(string regla, int? medida, int? maximo, string sujeto, string limite, TipoComponente a, TipoComponente b) =>
        medida is null || maximo is null
            ? SinDato(regla, "medidas", a, b)
            : medida > maximo
                ? new(regla, Severidad.Error, $"{sujeto} {medida} mm y {limite} {maximo} mm: no entra.", [a, b])
                : new(regla, Severidad.Correcto, $"{sujeto} {medida} mm; {limite} {maximo} mm (sobran {maximo - medida} mm).", [a, b]);

    private static Hallazgo Capacidad(string regla, int usados, int? disponibles, string que, string donde) =>
        disponibles is null
            ? SinDato(regla, donde, TipoComponente.Almacenamiento, TipoComponente.PlacaMadre)
            : usados > disponibles
                ? new(regla, Severidad.Error, $"Elegiste {usados} {que} y la placa tiene {disponibles} {donde}.", [TipoComponente.Almacenamiento, TipoComponente.PlacaMadre])
                : new(regla, Severidad.Correcto, $"{usados} de {disponibles} {donde} ocupados.", [TipoComponente.Almacenamiento, TipoComponente.PlacaMadre]);

    private static Hallazgo SinDato(string regla, string dato, params TipoComponente[] tipos) =>
        new(regla, Severidad.SinVerificar, $"No se pudo verificar: falta el dato «{dato}» en alguna ficha. Consúltanos antes de comprar.", tipos);

    private static string[] Lista(string v) => v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
