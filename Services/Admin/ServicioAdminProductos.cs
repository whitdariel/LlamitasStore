using System.ComponentModel.DataAnnotations;
using LlamitasStore.Data;
using LlamitasStore.Models.Entities;
using LlamitasStore.Utilidades;
using LlamitasStore.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace LlamitasStore.Services.Admin;

public class ProductoFormVm : IValidatableObject
{
    public int? Id { get; set; }

    [Required(ErrorMessage = Mensajes.Requerido), StringLength(40, ErrorMessage = Mensajes.Largo)]
    [RegularExpression(@"^[A-Za-z0-9\-]+$", ErrorMessage = "Usa solo letras, números y guiones.")]
    [Display(Name = "SKU")] public string Sku { get; set; } = string.Empty;

    [Required(ErrorMessage = Mensajes.Requerido), StringLength(150, ErrorMessage = Mensajes.Largo)]
    [Display(Name = "Nombre")] public string Nombre { get; set; } = string.Empty;

    [StringLength(80, ErrorMessage = Mensajes.Largo)]
    [Display(Name = "Modelo del fabricante")] public string? Modelo { get; set; }

    [Required(ErrorMessage = Mensajes.Requerido), StringLength(300, ErrorMessage = Mensajes.Largo)]
    [Display(Name = "Descripción corta")] public string DescripcionCorta { get; set; } = string.Empty;

    [Required(ErrorMessage = Mensajes.Requerido), StringLength(4000, ErrorMessage = Mensajes.Largo)]
    [Display(Name = "Descripción")] public string Descripcion { get; set; } = string.Empty;

    [Range(1, int.MaxValue, ErrorMessage = "Elige una categoría.")] [Display(Name = "Categoría")] public int CategoriaId { get; set; }
    [Range(1, int.MaxValue, ErrorMessage = "Elige una marca.")] [Display(Name = "Marca")] public int MarcaId { get; set; }

    [Range(typeof(decimal), "0.01", "999999", ErrorMessage = "El precio debe estar entre {1} y {2}.")]
    [Display(Name = "Precio (Bs.)")] public decimal Precio { get; set; }

    [Range(typeof(decimal), "0.01", "999999", ErrorMessage = "El precio de oferta debe estar entre {1} y {2}.")]
    [Display(Name = "Precio de oferta (opcional)")] public decimal? PrecioOferta { get; set; }

    [Range(typeof(decimal), "0", "999999", ErrorMessage = "El costo debe estar entre {1} y {2}.")]
    [Display(Name = "Costo (Bs.)")] public decimal PrecioCosto { get; set; }

    [Range(0, 100000, ErrorMessage = "Entre {1} y {2}.")]
    [Display(Name = "Stock inicial")] public int StockInicial { get; set; }

    [Range(0, 10000, ErrorMessage = "Entre {1} y {2}.")]
    [Display(Name = "Stock mínimo (alerta)")] public int StockMinimo { get; set; } = 3;

    [Range(0, 120, ErrorMessage = "Entre {1} y {2} meses.")]
    [Display(Name = "Garantía (meses)")] public int GarantiaMeses { get; set; } = 12;

    [Display(Name = "Estado")] public EstadoProducto Estado { get; set; } = EstadoProducto.Activo;
    [Display(Name = "Destacado en el inicio")] public bool Destacado { get; set; }
    [Display(Name = "Marcar como nuevo")] public bool EsNuevo { get; set; }

    /// <summary>Una especificación por línea: "Grupo | Nombre | Valor".</summary>
    [StringLength(8000, ErrorMessage = Mensajes.Largo)]
    [Display(Name = "Especificaciones")] public string? Especificaciones { get; set; }

    // Compatibilidad (PC Builder). Listas separadas por comas.
    [StringLength(20)] public string? Socket { get; set; }
    [StringLength(30)] public string? TiposMemoria { get; set; }
    [StringLength(20)] public string? FactorForma { get; set; }
    [StringLength(80)] public string? FactoresFormaSoportados { get; set; }
    [StringLength(120)] public string? SocketsSoportados { get; set; }
    [StringLength(20)] public string? Interfaz { get; set; }
    [Range(0, 16)] public int? SlotsMemoria { get; set; }
    [Range(0, 4096)] public int? MemoriaMaximaGb { get; set; }
    [Range(0, 2048)] public int? CapacidadGb { get; set; }
    [Range(0, 16)] public int? Modulos { get; set; }
    [Range(0, 20000)] public int? VelocidadMhz { get; set; }
    [Range(0, 3000)] public int? ConsumoW { get; set; }
    [Range(0, 3000)] public int? PotenciaW { get; set; }
    [Range(0, 3000)] public int? FuenteRecomendadaW { get; set; }
    [Range(0, 1000)] public int? LargoMm { get; set; }
    [Range(0, 1000)] public int? LargoGpuMaximoMm { get; set; }
    [Range(0, 500)] public int? AlturaMm { get; set; }
    [Range(0, 500)] public int? AlturaCoolerMaximaMm { get; set; }
    [Range(0, 16)] public int? SlotsM2 { get; set; }
    [Range(0, 32)] public int? PuertosSata { get; set; }
    public bool? GraficosIntegrados { get; set; }
    public bool? IncluyeCooler { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext contexto)
    {
        if (PrecioOferta is not null && PrecioOferta >= Precio)
            yield return new ValidationResult("El precio de oferta debe ser menor al precio normal.", [nameof(PrecioOferta)]);
        foreach (var (linea, i) in LineasEspecificacion().Select((l, i) => (l, i)))
            if (linea.Error) yield return new ValidationResult($"Especificación línea {i + 1}: usa el formato «Grupo | Nombre | Valor».", [nameof(Especificaciones)]);
    }

    public IEnumerable<(string Grupo, string Nombre, string Valor, bool Error)> LineasEspecificacion() =>
        (Especificaciones ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(l => l.Split('|', StringSplitOptions.TrimEntries))
            .Select(p => p.Length == 3 && p.All(x => x.Length > 0) && p[0].Length <= 60 && p[1].Length <= 80 && p[2].Length <= 300
                ? (p[0], p[1], p[2], false) : ("", "", "", true));

    public bool TieneCompatibilidad =>
        new object?[] { Socket, TiposMemoria, FactorForma, FactoresFormaSoportados, SocketsSoportados, Interfaz, SlotsMemoria, MemoriaMaximaGb,
            CapacidadGb, Modulos, VelocidadMhz, ConsumoW, PotenciaW, FuenteRecomendadaW, LargoMm, LargoGpuMaximoMm, AlturaMm,
            AlturaCoolerMaximaMm, SlotsM2, PuertosSata, GraficosIntegrados, IncluyeCooler }
            .Any(v => v is not null && v is not "");

    public static ProductoFormVm Desde(Producto p)
    {
        var c = p.Compatibilidad;
        return new ProductoFormVm
        {
            Id = p.Id, Sku = p.Sku, Nombre = p.Nombre, Modelo = p.Modelo, DescripcionCorta = p.DescripcionCorta, Descripcion = p.Descripcion,
            CategoriaId = p.CategoriaId, MarcaId = p.MarcaId, Precio = p.Precio, PrecioOferta = p.PrecioOferta, PrecioCosto = p.PrecioCosto,
            StockMinimo = p.StockMinimo, GarantiaMeses = p.GarantiaMeses, Estado = p.Estado, Destacado = p.Destacado, EsNuevo = p.EsNuevo,
            Especificaciones = string.Join('\n', p.Especificaciones.OrderBy(e => e.Orden).Select(e => $"{e.Grupo} | {e.Nombre} | {e.Valor}")),
            Socket = c?.Socket, TiposMemoria = c?.TiposMemoria, FactorForma = c?.FactorForma, FactoresFormaSoportados = c?.FactoresFormaSoportados,
            SocketsSoportados = c?.SocketsSoportados, Interfaz = c?.Interfaz, SlotsMemoria = c?.SlotsMemoria, MemoriaMaximaGb = c?.MemoriaMaximaGb,
            CapacidadGb = c?.CapacidadGb, Modulos = c?.Modulos, VelocidadMhz = c?.VelocidadMhz, ConsumoW = c?.ConsumoW, PotenciaW = c?.PotenciaW,
            FuenteRecomendadaW = c?.FuenteRecomendadaW, LargoMm = c?.LargoMm, LargoGpuMaximoMm = c?.LargoGpuMaximoMm, AlturaMm = c?.AlturaMm,
            AlturaCoolerMaximaMm = c?.AlturaCoolerMaximaMm, SlotsM2 = c?.SlotsM2, PuertosSata = c?.PuertosSata,
            GraficosIntegrados = c?.GraficosIntegrados, IncluyeCooler = c?.IncluyeCooler
        };
    }
}

public class ServicioAdminProductos
{
    public const long TamanoMaximoImagen = 2 * 1024 * 1024;
    public const int MaximoImagenes = 8;
    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _entorno;
    private readonly TimeProvider _reloj;

    public ServicioAdminProductos(AppDbContext db, IWebHostEnvironment entorno, TimeProvider reloj)
    {
        _db = db;
        _entorno = entorno;
        _reloj = reloj;
    }

    public Task<Producto?> ObtenerAsync(int id, CancellationToken ct = default) =>
        _db.Productos.Include(p => p.Especificaciones).Include(p => p.Compatibilidad).Include(p => p.Imagenes)
            .Include(p => p.Categoria).AsSplitQuery().FirstOrDefaultAsync(p => p.Id == id, ct);

    /// <summary>Crea o actualiza. Devuelve (id, errores por campo).</summary>
    public async Task<(int? Id, Dictionary<string, string> Errores)> GuardarAsync(ProductoFormVm vm, int usuarioId, CancellationToken ct = default)
    {
        var errores = new Dictionary<string, string>();
        var sku = vm.Sku.Trim().ToUpperInvariant();
        var slug = Slugs.Generar(vm.Nombre);
        if (string.IsNullOrEmpty(slug)) errores[nameof(vm.Nombre)] = "El nombre debe tener letras o números.";
        if (await _db.Productos.AnyAsync(p => p.Sku == sku && p.Id != vm.Id, ct)) errores[nameof(vm.Sku)] = "Ya existe un producto con ese SKU.";
        if (await _db.Productos.AnyAsync(p => p.Slug == slug && p.Id != vm.Id, ct)) errores[nameof(vm.Nombre)] = "Ya existe un producto con un nombre equivalente (misma URL).";
        if (!await _db.Categorias.AnyAsync(c => c.Id == vm.CategoriaId, ct)) errores[nameof(vm.CategoriaId)] = "Elige una categoría válida.";
        if (!await _db.Marcas.AnyAsync(m => m.Id == vm.MarcaId, ct)) errores[nameof(vm.MarcaId)] = "Elige una marca válida.";
        if (errores.Count > 0) return (null, errores);

        var id = await _db.EnTransaccionAsync(async c =>
        {
            var ahora = _reloj.GetUtcNow().UtcDateTime;
            Producto p;
            if (vm.Id is null)
            {
                p = new Producto { FechaCreacion = ahora, Stock = vm.StockInicial };
                _db.Productos.Add(p);
            }
            else
            {
                p = await _db.Productos.Include(x => x.Especificaciones).Include(x => x.Compatibilidad).FirstAsync(x => x.Id == vm.Id, c);
                if (p.Precio != vm.Precio || p.PrecioOferta != vm.PrecioOferta)
                {
                    _db.HistorialPrecios.Add(new HistorialPrecio
                    {
                        ProductoId = p.Id, PrecioAnterior = p.Precio, PrecioNuevo = vm.Precio,
                        PrecioOfertaAnterior = p.PrecioOferta, PrecioOfertaNuevo = vm.PrecioOferta, UsuarioId = usuarioId, Fecha = ahora
                    });
                }
                p.FechaActualizacion = ahora;
            }

            p.Sku = sku; p.Nombre = vm.Nombre.Trim(); p.Slug = slug; p.Modelo = Limpio(vm.Modelo);
            p.DescripcionCorta = vm.DescripcionCorta.Trim(); p.Descripcion = vm.Descripcion.Trim();
            p.CategoriaId = vm.CategoriaId; p.MarcaId = vm.MarcaId;
            p.Precio = vm.Precio; p.PrecioOferta = vm.PrecioOferta; p.PrecioCosto = vm.PrecioCosto;
            p.StockMinimo = vm.StockMinimo; p.GarantiaMeses = vm.GarantiaMeses; p.Estado = vm.Estado;
            p.Destacado = vm.Destacado; p.EsNuevo = vm.EsNuevo;

            _db.ProductoEspecificaciones.RemoveRange(p.Especificaciones);
            p.Especificaciones = vm.LineasEspecificacion().Where(l => !l.Error)
                .Select((l, i) => new ProductoEspecificacion { Grupo = l.Grupo, Nombre = l.Nombre, Valor = l.Valor, Orden = i }).ToList();

            if (vm.TieneCompatibilidad)
            {
                var cp = p.Compatibilidad ?? new ProductoCompatibilidad();
                cp.Socket = Limpio(vm.Socket); cp.TiposMemoria = Lista(vm.TiposMemoria); cp.FactorForma = Limpio(vm.FactorForma);
                cp.FactoresFormaSoportados = Lista(vm.FactoresFormaSoportados); cp.SocketsSoportados = Lista(vm.SocketsSoportados);
                cp.Interfaz = Limpio(vm.Interfaz); cp.SlotsMemoria = vm.SlotsMemoria; cp.MemoriaMaximaGb = vm.MemoriaMaximaGb;
                cp.CapacidadGb = vm.CapacidadGb; cp.Modulos = vm.Modulos; cp.VelocidadMhz = vm.VelocidadMhz; cp.ConsumoW = vm.ConsumoW;
                cp.PotenciaW = vm.PotenciaW; cp.FuenteRecomendadaW = vm.FuenteRecomendadaW; cp.LargoMm = vm.LargoMm;
                cp.LargoGpuMaximoMm = vm.LargoGpuMaximoMm; cp.AlturaMm = vm.AlturaMm; cp.AlturaCoolerMaximaMm = vm.AlturaCoolerMaximaMm;
                cp.SlotsM2 = vm.SlotsM2; cp.PuertosSata = vm.PuertosSata; cp.GraficosIntegrados = vm.GraficosIntegrados; cp.IncluyeCooler = vm.IncluyeCooler;
                p.Compatibilidad = cp;
            }
            else if (p.Compatibilidad is not null)
            {
                _db.ProductoCompatibilidades.Remove(p.Compatibilidad);
            }

            await _db.SaveChangesAsync(c);

            if (vm.Id is null && vm.StockInicial > 0)
            {
                _db.MovimientosInventario.Add(new MovimientoInventario
                {
                    ProductoId = p.Id, Tipo = TipoMovimiento.Entrada, Cantidad = vm.StockInicial, StockResultante = vm.StockInicial,
                    Motivo = "Stock inicial", UsuarioId = usuarioId, Fecha = ahora
                });
                await _db.SaveChangesAsync(c);
            }
            return p.Id;
        }, ct);
        return (id, errores);
    }

    /// <summary>
    /// Valida y guarda una imagen. Se comprueba el contenido real (firma binaria), no solo la extensión:
    /// solo JPG, PNG y WebP (sin SVG, que puede contener scripts). El nombre del archivo es aleatorio.
    /// </summary>
    public async Task<string?> SubirImagenAsync(int productoId, IFormFile? archivo, string? textoAlternativo, CancellationToken ct = default)
    {
        if (archivo is null || archivo.Length == 0) return "Elige una imagen.";
        if (archivo.Length > TamanoMaximoImagen) return "La imagen supera 2 MB.";

        var producto = await _db.Productos.Include(p => p.Imagenes).FirstOrDefaultAsync(p => p.Id == productoId, ct);
        if (producto is null) return "El producto no existe.";
        if (producto.Imagenes.Count >= MaximoImagenes) return $"Máximo {MaximoImagenes} imágenes por producto.";

        await using var flujo = archivo.OpenReadStream();
        var cabecera = new byte[12];
        var leidos = await flujo.ReadAsync(cabecera, ct);
        var extension = DetectarFormato(cabecera.AsSpan(0, leidos));
        if (extension is null) return "Formato no permitido: sube un JPG, PNG o WebP.";

        var carpetaRelativa = Path.Combine("uploads", "productos");
        var carpeta = Path.Combine(_entorno.WebRootPath, carpetaRelativa);
        Directory.CreateDirectory(carpeta);
        var nombre = $"{Guid.NewGuid():N}{extension}";
        flujo.Position = 0;
        await using (var destino = File.Create(Path.Combine(carpeta, nombre)))
            await flujo.CopyToAsync(destino, ct);

        producto.Imagenes.Add(new ProductoImagen
        {
            Url = $"/uploads/productos/{nombre}",
            TextoAlternativo = string.IsNullOrWhiteSpace(textoAlternativo) ? producto.Nombre : textoAlternativo.Trim()[..Math.Min(200, textoAlternativo.Trim().Length)],
            Orden = producto.Imagenes.Count == 0 ? 0 : producto.Imagenes.Max(i => i.Orden) + 1,
            EsPrincipal = producto.Imagenes.Count == 0
        });
        await _db.SaveChangesAsync(ct);
        return null;
    }

    public async Task<string?> MarcarImagenPrincipalAsync(int productoId, int imagenId, CancellationToken ct = default)
    {
        var imagenes = await _db.ProductoImagenes.Where(i => i.ProductoId == productoId).ToListAsync(ct);
        if (imagenes.All(i => i.Id != imagenId)) return "La imagen no existe.";
        foreach (var i in imagenes) i.EsPrincipal = i.Id == imagenId;
        await _db.SaveChangesAsync(ct);
        return null;
    }

    public async Task<string?> EliminarImagenAsync(int productoId, int imagenId, CancellationToken ct = default)
    {
        var imagen = await _db.ProductoImagenes.FirstOrDefaultAsync(i => i.Id == imagenId && i.ProductoId == productoId, ct);
        if (imagen is null) return "La imagen no existe.";
        _db.ProductoImagenes.Remove(imagen);
        if (imagen.EsPrincipal)
        {
            var otra = await _db.ProductoImagenes.Where(i => i.ProductoId == productoId && i.Id != imagenId).OrderBy(i => i.Orden).FirstOrDefaultAsync(ct);
            if (otra is not null) otra.EsPrincipal = true;
        }
        await _db.SaveChangesAsync(ct);

        // Solo se borran del disco las imágenes subidas por el panel (las ilustraciones del catálogo se conservan).
        if (imagen.Url.StartsWith("/uploads/productos/", StringComparison.Ordinal))
        {
            var ruta = Path.Combine(_entorno.WebRootPath, "uploads", "productos", Path.GetFileName(imagen.Url));
            if (File.Exists(ruta)) File.Delete(ruta);
        }
        return null;
    }

    internal static string? DetectarFormato(ReadOnlySpan<byte> b)
    {
        if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return ".jpg";
        if (b.Length >= 8 && b[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) return ".png";
        if (b.Length >= 12 && b[..4].SequenceEqual("RIFF"u8) && b[8..12].SequenceEqual("WEBP"u8)) return ".webp";
        return null;
    }

    private static string? Limpio(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
    private static string? Lista(string? v) => string.IsNullOrWhiteSpace(v)
        ? null
        : string.Join(",", v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
