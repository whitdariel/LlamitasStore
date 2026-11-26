using System.Globalization;
using LlamitasStore.Data;
using LlamitasStore.Models.Entities;
using LlamitasStore.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace LlamitasStore.Services.Catalogo;

public record FilaComparacion(string Grupo, string Nombre, IReadOnlyList<string?> Valores)
{
    /// <summary>Hay al menos dos valores distintos (un dato ausente cuenta como distinto de uno presente).</summary>
    public bool Difiere => Valores.Select(v => v?.Trim().ToLowerInvariant()).Distinct().Count() > 1;
}

public record Comparacion(IReadOnlyList<TarjetaProducto> Productos, IReadOnlyList<int> GarantiaMeses,
    IReadOnlyList<FilaComparacion> Filas, bool CategoriasMezcladas)
{
    public static readonly Comparacion Vacia = new([], [], [], false);
}

/// <summary>
/// Comparador de hasta 4 productos. La lista viaja en la URL (?ids=1,2) para compartir y se recuerda
/// en una cookie (solo ids, sin datos personales) para ir agregando desde el catálogo.
/// </summary>
public class ServicioComparador
{
    public const int Maximo = 4;
    public const string Cookie = ".LlamitasStore.Comparar";

    private readonly AppDbContext _db;
    private readonly ServicioCatalogo _catalogo;

    public ServicioComparador(AppDbContext db, ServicioCatalogo catalogo)
    {
        _db = db;
        _catalogo = catalogo;
    }

    /// <summary>Ids válidos, sin repetidos y como máximo <see cref="Maximo"/>.</summary>
    public static List<int> LeerIds(string? valor) =>
        (valor ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : 0)
            .Where(id => id > 0).Distinct().Take(Maximo).ToList();

    public static string Escribir(IEnumerable<int> ids) => string.Join(",", ids.Take(Maximo));

    public async Task<Comparacion> CompararAsync(IReadOnlyList<int> ids, CancellationToken ct = default)
    {
        if (ids.Count == 0) return Comparacion.Vacia;

        var productos = await _db.Productos.AsNoTracking()
            .Where(p => ids.Contains(p.Id) && p.Estado != EstadoProducto.Inactivo)
            .Include(p => p.Especificaciones)
            .Include(p => p.Compatibilidad)
            .AsSplitQuery()
            .ToListAsync(ct);
        var porId = productos.ToDictionary(p => p.Id);
        var orden = ids.Where(porId.ContainsKey).ToList();
        if (orden.Count == 0) return Comparacion.Vacia;

        var tarjetas = await _catalogo.ObtenerTarjetasAsync(orden, ct: ct);
        var lista = orden.Select(id => porId[id]).ToList();

        var filas = new List<FilaComparacion>();
        // 1) Datos técnicos tipados (los mismos que usa el PC Builder), solo si alguno los tiene.
        foreach (var (nombre, valor) in CamposCompatibilidad)
        {
            var valores = lista.Select(p => p.Compatibilidad is null ? null : valor(p.Compatibilidad)).ToList();
            if (valores.Any(v => v is not null)) filas.Add(new("Compatibilidad", nombre, valores));
        }
        // 2) Especificaciones de texto: unión por grupo y nombre, en el orden en que aparecen.
        var claves = lista.SelectMany(p => p.Especificaciones.OrderBy(e => e.Orden).Select(e => (e.Grupo, e.Nombre)))
            .Distinct(new ClaveSinMayusculas()).ToList();
        foreach (var (grupo, nombre) in claves)
        {
            var valores = lista.Select(p => p.Especificaciones.FirstOrDefault(e =>
                string.Equals(e.Grupo, grupo, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(e.Nombre, nombre, StringComparison.OrdinalIgnoreCase))?.Valor).ToList();
            // Si repite exactamente una fila de compatibilidad (p. ej. Socket), no se muestra dos veces.
            var repetida = filas.Any(f => f.Grupo == "Compatibilidad"
                && string.Equals(f.Nombre, nombre, StringComparison.OrdinalIgnoreCase)
                && f.Valores.SequenceEqual(valores, StringComparer.OrdinalIgnoreCase));
            if (!repetida) filas.Add(new(grupo, nombre, valores));
        }

        return new Comparacion(tarjetas, lista.Select(p => p.GarantiaMeses).ToList(), filas,
            lista.Select(p => p.CategoriaId).Distinct().Count() > 1);
    }

    private static readonly (string, Func<ProductoCompatibilidad, string?>)[] CamposCompatibilidad =
    [
        ("Socket", c => c.Socket),
        ("Memoria", c => c.TiposMemoria?.Replace(",", " / ")),
        ("Capacidad", c => c.CapacidadGb is { } gb ? $"{gb} GB" + (c.Modulos is > 1 ? $" ({c.Modulos} módulos)" : "") : null),
        ("Velocidad", c => c.VelocidadMhz is { } v ? $"{v} MT/s" : null),
        ("Formato de placa", c => c.FactorForma),
        ("Placas admitidas", c => c.FactoresFormaSoportados?.Replace(",", ", ")),
        ("Ranuras de RAM", c => c.SlotsMemoria?.ToString(CultureInfo.InvariantCulture)),
        ("Memoria máxima", c => c.MemoriaMaximaGb is { } m ? $"{m} GB" : null),
        ("Ranuras M.2", c => c.SlotsM2?.ToString(CultureInfo.InvariantCulture)),
        ("Puertos SATA", c => c.PuertosSata?.ToString(CultureInfo.InvariantCulture)),
        ("Interfaz", c => c.Interfaz),
        ("Consumo máximo", c => c.ConsumoW is { } w ? $"{w} W" : null),
        ("Potencia", c => c.PotenciaW is { } w ? $"{w} W" : null),
        ("Fuente recomendada", c => c.FuenteRecomendadaW is { } w ? $"{w} W" : null),
        ("Largo", c => c.LargoMm is { } l ? $"{l} mm" : null),
        ("GPU máxima", c => c.LargoGpuMaximoMm is { } l ? $"{l} mm" : null),
        ("Altura", c => c.AlturaMm is { } a ? $"{a} mm" : null),
        ("Disipador máximo", c => c.AlturaCoolerMaximaMm is { } a ? $"{a} mm" : null),
        ("Sockets de montaje", c => c.SocketsSoportados?.Replace(",", ", ")),
        ("Gráficos integrados", c => c.GraficosIntegrados is { } g ? (g ? "Sí" : "No") : null),
        ("Disipador incluido", c => c.IncluyeCooler is { } i ? (i ? "Sí" : "No") : null)
    ];

    private sealed class ClaveSinMayusculas : IEqualityComparer<(string, string)>
    {
        public bool Equals((string, string) a, (string, string) b) =>
            string.Equals(a.Item1, b.Item1, StringComparison.OrdinalIgnoreCase) && string.Equals(a.Item2, b.Item2, StringComparison.OrdinalIgnoreCase);
        public int GetHashCode((string, string) v) =>
            HashCode.Combine(v.Item1.ToLowerInvariant(), v.Item2.ToLowerInvariant());
    }
}

/// <summary>Lista del comparador de la petición actual (cookie). La usan el botón de las tarjetas y el header.</summary>
public class ComparadorActual
{
    private readonly IHttpContextAccessor _http;
    private List<int>? _ids;

    public ComparadorActual(IHttpContextAccessor http) => _http = http;

    public IReadOnlyList<int> Ids => _ids ??= ServicioComparador.LeerIds(_http.HttpContext?.Request.Cookies[ServicioComparador.Cookie]);

    public void Guardar(IEnumerable<int> ids)
    {
        _ids = ids.Distinct().Take(ServicioComparador.Maximo).ToList();
        var ctx = _http.HttpContext!;
        if (_ids.Count == 0)
        {
            ctx.Response.Cookies.Delete(ServicioComparador.Cookie);
            return;
        }
        ctx.Response.Cookies.Append(ServicioComparador.Cookie, ServicioComparador.Escribir(_ids), new CookieOptions
        {
            HttpOnly = true, SameSite = SameSiteMode.Lax, Secure = ctx.Request.IsHttps, IsEssential = true,
            MaxAge = TimeSpan.FromDays(30)
        });
    }
}
