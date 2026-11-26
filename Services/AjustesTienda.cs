using System.Globalization;
using LlamitasStore.Data;
using LlamitasStore.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace LlamitasStore.Services;

/// <summary>Ajustes generales de la tienda (tabla ajuste_tienda) con caché de 5 minutos.</summary>
public class AjustesTienda
{
    private const string ClaveCache = "ajustes-tienda";
    private static readonly CultureInfo Cultura = CultureInfo.GetCultureInfo("es-BO");

    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;
    private IReadOnlyDictionary<string, string>? _valores;

    public AjustesTienda(AppDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    private IReadOnlyDictionary<string, string> Valores =>
        _valores ??= _cache.GetOrCreate(ClaveCache, e =>
        {
            e.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
            return (IReadOnlyDictionary<string, string>)_db.Ajustes.AsNoTracking().ToDictionary(a => a.Clave, a => a.Valor);
        })!;

    public string Obtener(string clave, string porDefecto = "") =>
        Valores.TryGetValue(clave, out var v) && !string.IsNullOrWhiteSpace(v) ? v : porDefecto;

    public decimal ObtenerDecimal(string clave, decimal porDefecto = 0) =>
        decimal.TryParse(Obtener(clave), NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : porDefecto;

    public string NombreTienda => Obtener(ClavesAjuste.NombreTienda, "LlamitasStore");
    public string Moneda => Obtener(ClavesAjuste.SimboloMoneda, "Bs.");

    /// <summary>1590 → "Bs. 1.590,00".</summary>
    public string Dinero(decimal monto) => $"{Moneda} {monto.ToString("N2", Cultura)}";

    public void Invalidar() => _cache.Remove(ClaveCache);
}
