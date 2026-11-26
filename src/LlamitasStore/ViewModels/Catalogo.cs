using System.Globalization;
using System.Text;
using LlamitasStore.Models.Entities;
using LlamitasStore.Services.Catalogo;

namespace LlamitasStore.ViewModels;

public static class OrdenCatalogo
{
    public const string Relevancia = "relevancia";
    public const string PrecioAsc = "precio-asc";
    public const string PrecioDesc = "precio-desc";
    public const string Nuevos = "nuevos";
    public const string Valoracion = "valoracion";
    public const string Vendidos = "vendidos";
    public const string Nombre = "nombre";

    public static readonly (string Valor, string Texto)[] Opciones =
    [
        (Relevancia, "Relevancia"), (PrecioAsc, "Precio: menor a mayor"), (PrecioDesc, "Precio: mayor a menor"),
        (Nuevos, "Novedades"), (Valoracion, "Mejor valorados"), (Vendidos, "Más vendidos"), (Nombre, "Nombre A-Z")
    ];

    public static bool EsValido(string? orden) => Opciones.Any(o => o.Valor == orden);
}

/// <summary>Filtros del catálogo. Se enlaza desde la query string (GET), así cada búsqueda es un enlace compartible.</summary>
public class FiltroCatalogo
{
    public const int TamanoPagina = 12;
    public const int LargoMaximoBusqueda = 100;

    public string? Q { get; set; }
    public string? Categoria { get; set; }
    public List<string> Marca { get; set; } = new();
    public decimal? Min { get; set; }
    public decimal? Max { get; set; }
    public bool Disponible { get; set; }
    public bool Oferta { get; set; }
    public string? Socket { get; set; }
    public string? Memoria { get; set; }
    public int? Valoracion { get; set; }
    public string? Orden { get; set; }
    public int Pagina { get; set; } = 1;

    /// <summary>Corrige valores fuera de rango en lugar de fallar: los enlaces viejos o editados a mano siguen funcionando.</summary>
    public FiltroCatalogo Normalizar()
    {
        Q = string.IsNullOrWhiteSpace(Q) ? null : Q.Trim();
        if (Q is { Length: > LargoMaximoBusqueda }) Q = Q[..LargoMaximoBusqueda];
        Categoria = string.IsNullOrWhiteSpace(Categoria) ? null : Categoria.Trim().ToLowerInvariant();
        Marca = Marca.Where(m => !string.IsNullOrWhiteSpace(m)).Select(m => m.Trim().ToLowerInvariant()).Distinct().Take(20).ToList();
        if (Min < 0) Min = null;
        if (Max < 0) Max = null;
        if (Min is not null && Max is not null && Min > Max) (Min, Max) = (Max, Min);
        Socket = string.IsNullOrWhiteSpace(Socket) ? null : Socket.Trim();
        Memoria = string.IsNullOrWhiteSpace(Memoria) ? null : Memoria.Trim();
        if (Valoracion is < 1 or > 5) Valoracion = null;
        if (!OrdenCatalogo.EsValido(Orden)) Orden = OrdenCatalogo.Relevancia;
        if (Pagina < 1) Pagina = 1;
        return this;
    }

    public FiltroCatalogo Clonar() => new()
    {
        Q = Q, Categoria = Categoria, Marca = new List<string>(Marca), Min = Min, Max = Max, Disponible = Disponible,
        Oferta = Oferta, Socket = Socket, Memoria = Memoria, Valoracion = Valoracion, Orden = Orden, Pagina = Pagina
    };

    public bool TieneFiltros =>
        Q is not null || Marca.Count > 0 || Min is not null || Max is not null || Disponible || Oferta ||
        Socket is not null || Memoria is not null || Valoracion is not null;

    /// <summary>URL del catálogo con estos filtros aplicando un cambio (p. ej. otra página). Vuelve a la página 1 salvo que el cambio la fije.</summary>
    public string Url(Action<FiltroCatalogo>? cambio = null, bool reiniciarPagina = true)
    {
        var f = Clonar();
        if (reiniciarPagina) f.Pagina = 1;
        cambio?.Invoke(f);

        var ruta = f.Categoria is null ? "/catalogo" : $"/catalogo/{Uri.EscapeDataString(f.Categoria)}";
        var qs = new StringBuilder();
        void Add(string k, string? v)
        {
            if (string.IsNullOrEmpty(v)) return;
            qs.Append(qs.Length == 0 ? '?' : '&').Append(k).Append('=').Append(Uri.EscapeDataString(v));
        }

        Add("q", f.Q);
        foreach (var m in f.Marca) Add("marca", m);
        Add("min", f.Min?.ToString(CultureInfo.InvariantCulture));
        Add("max", f.Max?.ToString(CultureInfo.InvariantCulture));
        if (f.Disponible) Add("disponible", "true");
        if (f.Oferta) Add("oferta", "true");
        Add("socket", f.Socket);
        Add("memoria", f.Memoria);
        Add("valoracion", f.Valoracion?.ToString(CultureInfo.InvariantCulture));
        if (f.Orden != OrdenCatalogo.Relevancia) Add("orden", f.Orden);
        if (f.Pagina > 1) Add("pagina", f.Pagina.ToString(CultureInfo.InvariantCulture));

        return ruta + qs;
    }
}

public record TarjetaProducto(
    int Id, string Nombre, string Slug, string Sku, string Marca, string Categoria, string? ImagenUrl, string ImagenAlt,
    PrecioCalculado Precio, int Stock, int StockMinimo, bool EsNuevo, decimal Valoracion, int TotalResenas, string DescripcionCorta)
{
    public bool Disponible => Stock > 0;
    public bool UltimasUnidades => Stock > 0 && Stock <= StockMinimo;
}

public record Faceta(string Valor, string Texto, int Cantidad, bool Seleccionada);

public record ResultadoCatalogo(
    FiltroCatalogo Filtro,
    Categoria? Categoria,
    IReadOnlyList<TarjetaProducto> Productos,
    int Total,
    IReadOnlyList<Faceta> Categorias,
    IReadOnlyList<Faceta> Marcas,
    IReadOnlyList<Faceta> Sockets,
    IReadOnlyList<Faceta> Memorias,
    decimal? PrecioMinimo,
    decimal? PrecioMaximo)
{
    public int TotalPaginas => Math.Max(1, (int)Math.Ceiling(Total / (double)FiltroCatalogo.TamanoPagina));
    public int Desde => Total == 0 ? 0 : (Filtro.Pagina - 1) * FiltroCatalogo.TamanoPagina + 1;
    public int Hasta => Math.Min(Total, Filtro.Pagina * FiltroCatalogo.TamanoPagina);

    public string Titulo =>
        Categoria?.Nombre ?? (Filtro.Q is not null ? $"Resultados para “{Filtro.Q}”" : "Catálogo");
}

public record SugerenciaCategoria(string Nombre, string Slug);

public record Sugerencias(IReadOnlyList<SugerenciaCategoria> Categorias, IReadOnlyList<TarjetaProducto> Productos);

public record GrupoEspecificaciones(string Grupo, IReadOnlyList<(string Nombre, string Valor)> Filas);

public record ResenaVista(string Autor, int Calificacion, string Titulo, string Comentario, DateTime Fecha, bool CompraVerificada);

public record FichaProducto(
    Producto Producto,
    PrecioCalculado Precio,
    IReadOnlyList<ProductoImagen> Imagenes,
    IReadOnlyList<GrupoEspecificaciones> Especificaciones,
    IReadOnlyList<ResenaVista> Resenas,
    IReadOnlyDictionary<int, int> DistribucionEstrellas,
    IReadOnlyList<TarjetaProducto> Relacionados)
{
    public bool Comprable => Producto.Estado == EstadoProducto.Activo && Producto.Stock > 0;
}
