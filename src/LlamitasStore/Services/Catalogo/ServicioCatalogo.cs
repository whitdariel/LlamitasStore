using LlamitasStore.Data;
using LlamitasStore.Models.Entities;
using LlamitasStore.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace LlamitasStore.Services.Catalogo;

/// <summary>Búsqueda, filtros, facetas y fichas del catálogo público.</summary>
public class ServicioCatalogo
{
    private readonly AppDbContext _db;
    private readonly ServicioPrecios _precios;

    public ServicioCatalogo(AppDbContext db, ServicioPrecios precios)
    {
        _db = db;
        _precios = precios;
    }

    /// <summary>Fila liviana para filtrar y ordenar. El precio final depende de promociones, por eso se calcula en memoria.</summary>
    private sealed record Fila(
        int Id, string Nombre, string Sku, decimal Precio, decimal? PrecioOferta, int MarcaId, string MarcaSlug, string MarcaNombre,
        int CategoriaId, string CategoriaSlug, string CategoriaNombre, int Stock, decimal Valoracion, int TotalResenas,
        int TotalVendidos, DateTime FechaCreacion, bool Destacado, bool EsNuevo, string? Socket, string? TiposMemoria)
    {
        public PrecioCalculado PrecioCalculado { get; set; } = null!;
    }

    /// <returns>null si la categoría pedida no existe (el controlador responde 404).</returns>
    public async Task<ResultadoCatalogo?> BuscarAsync(FiltroCatalogo filtro, CancellationToken ct = default)
    {
        filtro.Normalizar();

        Categoria? categoria = null;
        if (filtro.Categoria is not null)
        {
            categoria = await _db.Categorias.AsNoTracking().FirstOrDefaultAsync(c => c.Slug == filtro.Categoria && c.Activa, ct);
            if (categoria is null) return null;
        }

        var promociones = await _precios.ObtenerPromocionesVigentesAsync(ct);

        // 1) Base en SQL: productos activos + búsqueda de texto. Sobre esta base se calculan las facetas.
        var consulta = _db.Productos.AsNoTracking()
            .Where(p => p.Estado == EstadoProducto.Activo && p.Categoria.Activa && p.Marca.Activa);
        consulta = AplicarBusqueda(consulta, filtro.Q);

        var filasBase = await consulta
            .Select(p => new Fila(p.Id, p.Nombre, p.Sku, p.Precio, p.PrecioOferta, p.MarcaId, p.Marca.Slug, p.Marca.Nombre,
                p.CategoriaId, p.Categoria.Slug, p.Categoria.Nombre, p.Stock, p.ValoracionPromedio, p.TotalResenas,
                p.TotalVendidos, p.FechaCreacion, p.Destacado, p.EsNuevo,
                p.Compatibilidad != null ? p.Compatibilidad.Socket : null,
                p.Compatibilidad != null ? p.Compatibilidad.TiposMemoria : null))
            .ToListAsync(ct);

        foreach (var f in filasBase)
            f.PrecioCalculado = ServicioPrecios.Calcular(f.Precio, f.PrecioOferta, f.Id, f.MarcaId, f.CategoriaId, promociones);

        // Facetas de categoría: sobre la búsqueda, antes de elegir categoría (para poder cambiarla).
        var facetasCategoria = await FacetasCategoriaAsync(filasBase, filtro.Categoria, ct);

        var enCategoria = categoria is null ? filasBase : filasBase.Where(f => f.CategoriaId == categoria.Id).ToList();

        // 2) Facetas dentro de la categoría elegida (no dependen de los filtros secundarios).
        var facetasMarca = enCategoria
            .GroupBy(f => (f.MarcaSlug, f.MarcaNombre))
            .Select(g => new Faceta(g.Key.MarcaSlug, g.Key.MarcaNombre, g.Count(), filtro.Marca.Contains(g.Key.MarcaSlug)))
            .OrderBy(f => f.Texto).ToList();
        var facetasSocket = Facetar(enCategoria.Select(f => f.Socket), filtro.Socket);
        var facetasMemoria = Facetar(enCategoria.SelectMany(f => Separar(f.TiposMemoria)), filtro.Memoria);
        var precioMin = enCategoria.Count > 0 ? enCategoria.Min(f => f.PrecioCalculado.PrecioFinal) : (decimal?)null;
        var precioMax = enCategoria.Count > 0 ? enCategoria.Max(f => f.PrecioCalculado.PrecioFinal) : (decimal?)null;

        // 3) Filtros combinables (AND entre filtros, OR entre marcas seleccionadas).
        IEnumerable<Fila> filas = enCategoria;
        if (filtro.Marca.Count > 0) filas = filas.Where(f => filtro.Marca.Contains(f.MarcaSlug));
        if (filtro.Min is not null) filas = filas.Where(f => f.PrecioCalculado.PrecioFinal >= filtro.Min);
        if (filtro.Max is not null) filas = filas.Where(f => f.PrecioCalculado.PrecioFinal <= filtro.Max);
        if (filtro.Disponible) filas = filas.Where(f => f.Stock > 0);
        if (filtro.Oferta) filas = filas.Where(f => f.PrecioCalculado.TieneDescuento);
        if (filtro.Socket is not null) filas = filas.Where(f => string.Equals(f.Socket, filtro.Socket, StringComparison.OrdinalIgnoreCase));
        if (filtro.Memoria is not null) filas = filas.Where(f => Separar(f.TiposMemoria).Contains(filtro.Memoria, StringComparer.OrdinalIgnoreCase));
        if (filtro.Valoracion is not null) filas = filas.Where(f => f.TotalResenas > 0 && f.Valoracion >= filtro.Valoracion);

        var ordenadas = Ordenar(filas, filtro).ToList();

        var total = ordenadas.Count;
        var totalPaginas = Math.Max(1, (int)Math.Ceiling(total / (double)FiltroCatalogo.TamanoPagina));
        if (filtro.Pagina > totalPaginas) filtro.Pagina = totalPaginas;

        var idsPagina = ordenadas
            .Skip((filtro.Pagina - 1) * FiltroCatalogo.TamanoPagina)
            .Take(FiltroCatalogo.TamanoPagina)
            .Select(f => f.Id)
            .ToList();

        var tarjetas = await ObtenerTarjetasAsync(idsPagina, promociones, ct);

        return new ResultadoCatalogo(filtro, categoria, tarjetas, total, facetasCategoria, facetasMarca,
            facetasSocket, facetasMemoria, precioMin, precioMax);
    }

    /// <summary>Tarjetas en el mismo orden que <paramref name="ids"/>. Reutilizable en inicio, favoritos y comparador.</summary>
    public async Task<IReadOnlyList<TarjetaProducto>> ObtenerTarjetasAsync(IReadOnlyCollection<int> ids,
        IReadOnlyList<PromocionVigente>? promociones = null, CancellationToken ct = default)
    {
        if (ids.Count == 0) return [];
        promociones ??= await _precios.ObtenerPromocionesVigentesAsync(ct);

        var datos = await _db.Productos.AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .Select(p => new
            {
                p.Id, p.Nombre, p.Slug, p.Sku, Marca = p.Marca.Nombre, Categoria = p.Categoria.Nombre,
                p.MarcaId, p.CategoriaId, p.Precio, p.PrecioOferta, p.Stock, p.StockMinimo, p.EsNuevo,
                p.ValoracionPromedio, p.TotalResenas, p.DescripcionCorta,
                Imagen = p.Imagenes.OrderByDescending(i => i.EsPrincipal).ThenBy(i => i.Orden)
                    .Select(i => new { i.Url, i.TextoAlternativo }).FirstOrDefault()
            })
            .ToDictionaryAsync(p => p.Id, ct);

        return ids.Where(datos.ContainsKey).Select(id =>
        {
            var p = datos[id];
            return new TarjetaProducto(p.Id, p.Nombre, p.Slug, p.Sku, p.Marca, p.Categoria, p.Imagen?.Url,
                p.Imagen?.TextoAlternativo ?? p.Nombre,
                ServicioPrecios.Calcular(p.Precio, p.PrecioOferta, p.Id, p.MarcaId, p.CategoriaId, promociones),
                p.Stock, p.StockMinimo, p.EsNuevo, p.ValoracionPromedio, p.TotalResenas, p.DescripcionCorta);
        }).ToList();
    }

    /// <returns>null si no existe o está inactivo. Los descontinuados se muestran (para enlaces viejos) pero no se pueden comprar.</returns>
    public async Task<FichaProducto?> ObtenerFichaAsync(string slug, CancellationToken ct = default)
    {
        var producto = await _db.Productos.AsNoTracking()
            .Include(p => p.Categoria)
            .Include(p => p.Marca)
            .Include(p => p.Imagenes)
            .Include(p => p.Especificaciones)
            .Include(p => p.Compatibilidad)
            .AsSplitQuery()
            .FirstOrDefaultAsync(p => p.Slug == slug && p.Estado != EstadoProducto.Inactivo, ct);
        if (producto is null) return null;

        var promociones = await _precios.ObtenerPromocionesVigentesAsync(ct);
        var precio = ServicioPrecios.Calcular(producto.Precio, producto.PrecioOferta, producto.Id, producto.MarcaId, producto.CategoriaId, promociones);

        var especificaciones = producto.Especificaciones
            .OrderBy(e => e.Orden)
            .GroupBy(e => e.Grupo)
            .Select(g => new GrupoEspecificaciones(g.Key, g.Select(e => (e.Nombre, e.Valor)).ToList()))
            .ToList();

        var resenasQuery = _db.Resenas.AsNoTracking().Where(r => r.ProductoId == producto.Id && r.Aprobada);
        var resenas = await resenasQuery
            .OrderByDescending(r => r.Fecha).Take(20)
            .Select(r => new ResenaVista(r.Usuario.Nombre + " " + r.Usuario.Apellido.Substring(0, 1) + ".",
                r.Calificacion, r.Titulo, r.Comentario, r.Fecha, r.CompraVerificada))
            .ToListAsync(ct);
        var distribucion = await resenasQuery
            .GroupBy(r => r.Calificacion)
            .Select(g => new { Estrellas = g.Key, Cantidad = g.Count() })
            .ToDictionaryAsync(x => x.Estrellas, x => x.Cantidad, ct);

        var relacionados = await ObtenerRelacionadosAsync(producto, promociones, ct);

        var imagenes = producto.Imagenes.OrderByDescending(i => i.EsPrincipal).ThenBy(i => i.Orden).ToList();
        return new FichaProducto(producto, precio, imagenes, especificaciones, resenas, distribucion, relacionados);
    }

    /// <summary>
    /// Relacionados: misma categoría; primero los que comparten socket (si aplica) o marca, luego destacados y más vendidos.
    /// Si faltan, se completa con destacados de otras categorías.
    /// </summary>
    public async Task<IReadOnlyList<TarjetaProducto>> ObtenerRelacionadosAsync(Producto producto,
        IReadOnlyList<PromocionVigente> promociones, CancellationToken ct = default, int cantidad = 4)
    {
        var socket = producto.Compatibilidad?.Socket;
        var candidatos = await _db.Productos.AsNoTracking()
            .Where(p => p.Id != producto.Id && p.Estado == EstadoProducto.Activo && p.CategoriaId == producto.CategoriaId)
            .Select(p => new
            {
                p.Id,
                Puntaje = (socket != null && p.Compatibilidad != null && p.Compatibilidad.Socket == socket ? 4 : 0)
                          + (p.MarcaId == producto.MarcaId ? 2 : 0)
                          + (p.Destacado ? 1 : 0)
                          + (p.Stock > 0 ? 1 : 0),
                p.TotalVendidos
            })
            .ToListAsync(ct);

        var ids = candidatos.OrderByDescending(c => c.Puntaje).ThenByDescending(c => c.TotalVendidos).ThenBy(c => c.Id)
            .Take(cantidad).Select(c => c.Id).ToList();

        if (ids.Count < cantidad)
        {
            // Completar con destacados del mismo "mundo": periféricos con periféricos, componentes con componentes.
            var tipo = producto.Categoria?.TipoComponente
                ?? await _db.Categorias.Where(c => c.Id == producto.CategoriaId).Select(c => c.TipoComponente).FirstAsync(ct);
            var esComponente = tipo != TipoComponente.Ninguno;
            var extra = await _db.Productos.AsNoTracking()
                .Where(p => p.Id != producto.Id && !ids.Contains(p.Id) && p.Estado == EstadoProducto.Activo && p.Stock > 0)
                .OrderByDescending(p => (p.Categoria.TipoComponente != TipoComponente.Ninguno) == esComponente)
                .ThenByDescending(p => p.Destacado)
                .ThenByDescending(p => p.TotalVendidos)
                .ThenBy(p => p.Id)
                .Select(p => p.Id).Take(cantidad - ids.Count).ToListAsync(ct);
            ids.AddRange(extra);
        }

        return await ObtenerTarjetasAsync(ids, promociones, ct);
    }

    /// <summary>Sugerencias del buscador del header: hasta 3 categorías y <paramref name="maxProductos"/> productos.</summary>
    public async Task<Sugerencias> SugerirAsync(string? q, int maxProductos = 6, CancellationToken ct = default)
    {
        q = q?.Trim();
        if (string.IsNullOrEmpty(q) || q.Length < 2) return new Sugerencias([], []);
        if (q.Length > FiltroCatalogo.LargoMaximoBusqueda) q = q[..FiltroCatalogo.LargoMaximoBusqueda];

        var patron = "%" + EscaparLike(q) + "%";
        var categorias = await _db.Categorias.AsNoTracking()
            .Where(c => c.Activa && EF.Functions.Like(c.Nombre, patron, "\\"))
            .OrderBy(c => c.Orden).Take(3)
            .Select(c => new SugerenciaCategoria(c.Nombre, c.Slug))
            .ToListAsync(ct);

        var inicio = EscaparLike(q) + "%";
        var ids = await AplicarBusqueda(_db.Productos.AsNoTracking()
                .Where(p => p.Estado == EstadoProducto.Activo && p.Categoria.Activa && p.Marca.Activa), q)
            .OrderByDescending(p => EF.Functions.Like(p.Nombre, inicio, "\\"))
            .ThenByDescending(p => p.Stock > 0)
            .ThenByDescending(p => p.Destacado)
            .ThenByDescending(p => p.TotalVendidos)
            .ThenBy(p => p.Nombre)
            .Select(p => p.Id)
            .Take(maxProductos)
            .ToListAsync(ct);

        return new Sugerencias(categorias, await ObtenerTarjetasAsync(ids, ct: ct));
    }

    internal static IQueryable<Producto> AplicarBusqueda(IQueryable<Producto> consulta, string? q)
    {
        if (string.IsNullOrWhiteSpace(q)) return consulta;

        // Cada palabra debe aparecer en algún campo (nombre, SKU, modelo, marca, categoría o descripción corta).
        var terminos = q.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(6);
        foreach (var termino in terminos)
        {
            var patron = "%" + EscaparLike(termino) + "%";
            consulta = consulta.Where(p =>
                EF.Functions.Like(p.Nombre, patron, "\\") ||
                EF.Functions.Like(p.Sku, patron, "\\") ||
                (p.Modelo != null && EF.Functions.Like(p.Modelo, patron, "\\")) ||
                EF.Functions.Like(p.Marca.Nombre, patron, "\\") ||
                EF.Functions.Like(p.Categoria.Nombre, patron, "\\") ||
                EF.Functions.Like(p.DescripcionCorta, patron, "\\"));
        }
        return consulta;
    }

    internal static string EscaparLike(string texto) =>
        texto.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    private static IEnumerable<Fila> Ordenar(IEnumerable<Fila> filas, FiltroCatalogo filtro) => filtro.Orden switch
    {
        OrdenCatalogo.PrecioAsc => filas.OrderBy(f => f.PrecioCalculado.PrecioFinal).ThenBy(f => f.Nombre),
        OrdenCatalogo.PrecioDesc => filas.OrderByDescending(f => f.PrecioCalculado.PrecioFinal).ThenBy(f => f.Nombre),
        OrdenCatalogo.Nuevos => filas.OrderByDescending(f => f.EsNuevo).ThenByDescending(f => f.FechaCreacion).ThenBy(f => f.Nombre),
        OrdenCatalogo.Valoracion => filas.OrderByDescending(f => f.Valoracion).ThenByDescending(f => f.TotalResenas).ThenBy(f => f.Nombre),
        OrdenCatalogo.Vendidos => filas.OrderByDescending(f => f.TotalVendidos).ThenBy(f => f.Nombre),
        OrdenCatalogo.Nombre => filas.OrderBy(f => f.Nombre, StringComparer.CurrentCultureIgnoreCase),
        _ => filas
            .OrderByDescending(f => filtro.Q is not null && f.Nombre.Contains(filtro.Q, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(f => f.Stock > 0)
            .ThenByDescending(f => f.Destacado)
            .ThenByDescending(f => f.TotalVendidos)
            .ThenBy(f => f.Nombre)
    };

    private async Task<IReadOnlyList<Faceta>> FacetasCategoriaAsync(List<Fila> filas, string? seleccionada, CancellationToken ct)
    {
        var conteo = filas.GroupBy(f => f.CategoriaSlug).ToDictionary(g => g.Key, g => g.Count());
        var categorias = await _db.Categorias.AsNoTracking().Where(c => c.Activa).OrderBy(c => c.Orden)
            .Select(c => new { c.Slug, c.Nombre }).ToListAsync(ct);
        return categorias
            .Where(c => conteo.ContainsKey(c.Slug) || c.Slug == seleccionada)
            .Select(c => new Faceta(c.Slug, c.Nombre, conteo.GetValueOrDefault(c.Slug), c.Slug == seleccionada))
            .ToList();
    }

    private static List<Faceta> Facetar(IEnumerable<string?> valores, string? seleccionado) =>
        valores.Where(v => !string.IsNullOrWhiteSpace(v))
            .GroupBy(v => v!, StringComparer.OrdinalIgnoreCase)
            .Select(g => new Faceta(g.Key, g.Key, g.Count(), string.Equals(g.Key, seleccionado, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(f => f.Texto)
            .ToList();

    internal static string[] Separar(string? lista) =>
        string.IsNullOrWhiteSpace(lista) ? [] : lista.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
