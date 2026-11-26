using System.Globalization;
using System.Text;
using LlamitasStore.Data;
using LlamitasStore.Models.Entities;
using LlamitasStore.Services.Catalogo;
using LlamitasStore.Services.Ventas;
using Microsoft.EntityFrameworkCore;

namespace LlamitasStore.Services.PcBuilder;

public record PasoPc(TipoComponente Tipo, string Clave, string Nombre, string Icono, string Descripcion, bool Multiple, int MaxCantidad);

/// <summary>Selección del armado. Viaja en la URL (cpu=3&amp;ram=12*2&amp;alm=20,22), así cualquier armado se puede compartir.</summary>
public sealed class SeleccionPc
{
    public static readonly PasoPc[] Pasos =
    [
        new(TipoComponente.Procesador, "cpu", "Procesador", "cat-cpu", "El procesador ejecuta todo lo que hace tu PC. Su socket define qué placas madre puedes usar.", false, 1),
        new(TipoComponente.PlacaMadre, "placa", "Placa madre", "cat-placa", "Conecta todos los componentes. Define el tipo de memoria, cuántas unidades puedes conectar y el tamaño del gabinete.", false, 1),
        new(TipoComponente.MemoriaRam, "ram", "Memoria RAM", "cat-ram", "Guarda los datos que el procesador usa al momento. Debe ser del tipo (DDR4 o DDR5) que admite tu placa.", false, 4),
        new(TipoComponente.TarjetaGrafica, "gpu", "Tarjeta gráfica", "cat-gpu", "Genera la imagen en juegos y programas 3D. Es opcional si el procesador tiene gráficos integrados.", false, 1),
        new(TipoComponente.Almacenamiento, "alm", "Almacenamiento", "cat-ssd", "Donde se instalan el sistema y tus archivos. Las unidades M.2 usan ranuras M.2; las SATA, puertos SATA.", true, 1),
        new(TipoComponente.FuentePoder, "psu", "Fuente de poder", "cat-psu", "Alimenta todo el equipo. Debe cubrir el consumo estimado con margen.", false, 1),
        new(TipoComponente.Gabinete, "gab", "Gabinete", "cat-gabinete", "La caja del equipo. Debe admitir el formato de tu placa, el largo de la gráfica y la altura del disipador.", false, 1),
        new(TipoComponente.Refrigeracion, "ref", "Refrigeración", "cat-cooler", "Disipa el calor del procesador. Algunos procesadores incluyen uno en la caja.", false, 1)
    ];

    public const int MaxUnidadesAlmacenamiento = 4;

    private readonly Dictionary<TipoComponente, List<(int Id, int Cantidad)>> _items = new();

    public IReadOnlyList<(int Id, int Cantidad)> De(TipoComponente t) => _items.TryGetValue(t, out var l) ? l : [];
    public IEnumerable<(TipoComponente Tipo, int Id, int Cantidad)> Todos => _items.SelectMany(kv => kv.Value.Select(v => (kv.Key, v.Id, v.Cantidad)));
    public bool Vacia => _items.Values.All(l => l.Count == 0);
    public int? ConfiguracionId { get; set; }

    public static PasoPc Paso(TipoComponente t) => Pasos.First(p => p.Tipo == t);
    public static PasoPc? Paso(string? clave) => Pasos.FirstOrDefault(p => p.Clave == clave);

    public static SeleccionPc Desde(IQueryCollection q)
    {
        var s = new SeleccionPc();
        foreach (var paso in Pasos)
        {
            var valor = q[paso.Clave].ToString();
            if (string.IsNullOrWhiteSpace(valor)) continue;
            // Repetidos e inválidos no consumen el cupo: Poner ignora duplicados y corta en MaxUnidadesAlmacenamiento.
            foreach (var parte in valor.Split(',', StringSplitOptions.RemoveEmptyEntries).Take(paso.Multiple ? 4 * MaxUnidadesAlmacenamiento : 1))
            {
                var trozos = parte.Split('*');
                if (!int.TryParse(trozos[0], out var id) || id <= 0) continue;
                var cant = trozos.Length > 1 && int.TryParse(trozos[1], out var c) ? Math.Clamp(c, 1, paso.MaxCantidad) : 1;
                s.Poner(paso.Tipo, id, cant, agregar: paso.Multiple);
            }
        }
        if (int.TryParse(q["config"], out var config) && config > 0) s.ConfiguracionId = config;
        return s;
    }

    public SeleccionPc Clonar()
    {
        var c = new SeleccionPc { ConfiguracionId = ConfiguracionId };
        foreach (var (t, l) in _items) c._items[t] = l.ToList();
        return c;
    }

    /// <summary>Componentes únicos se reemplazan; en almacenamiento <paramref name="agregar"/> suma una unidad más.</summary>
    public SeleccionPc Poner(TipoComponente t, int id, int cantidad = 1, bool agregar = false)
    {
        if (!_items.TryGetValue(t, out var l)) _items[t] = l = new();
        if (!agregar) l.Clear();
        if (l.Any(x => x.Id == id)) return this;
        if (l.Count >= MaxUnidadesAlmacenamiento) return this;
        l.Add((id, cantidad));
        return this;
    }

    public SeleccionPc Quitar(TipoComponente t, int? id = null)
    {
        if (_items.TryGetValue(t, out var l)) l.RemoveAll(x => id is null || x.Id == id);
        return this;
    }

    /// <summary>Pares clave/valor para enviar la selección en un formulario (campos ocultos).</summary>
    public IEnumerable<(string Clave, string Valor)> Campos()
    {
        foreach (var p in Pasos)
        {
            var l = De(p.Tipo);
            if (l.Count > 0) yield return (p.Clave, string.Join(",", l.Select(x => x.Cantidad > 1 ? $"{x.Id}*{x.Cantidad}" : x.Id.ToString(CultureInfo.InvariantCulture))));
        }
        if (ConfiguracionId is not null) yield return ("config", ConfiguracionId.Value.ToString(CultureInfo.InvariantCulture));
    }

    public string Query(string? paso = null, bool todos = false)
    {
        var sb = new StringBuilder();
        void Add(string k, string v) => sb.Append(sb.Length == 0 ? '?' : '&').Append(k).Append('=').Append(Uri.EscapeDataString(v));
        foreach (var p in Pasos)
        {
            var l = De(p.Tipo);
            if (l.Count > 0) Add(p.Clave, string.Join(",", l.Select(x => x.Cantidad > 1 ? $"{x.Id}*{x.Cantidad}" : x.Id.ToString(CultureInfo.InvariantCulture))));
        }
        if (ConfiguracionId is not null) Add("config", ConfiguracionId.Value.ToString(CultureInfo.InvariantCulture));
        if (paso is not null) Add("paso", paso);
        if (todos) Add("todos", "true"); // el enlace de modelos de bool acepta "true", no "1"
        return "/pc-builder" + sb;
    }
}

public record ComponenteVista(Componente Datos, string Slug, string Marca, string? Imagen, PrecioCalculado Precio, int Stock, int Cantidad)
{
    public decimal Subtotal => Precio.PrecioFinal * Cantidad;
    public bool SinStock => Stock < Cantidad;
}

public record ArmadoPc(SeleccionPc Seleccion, IReadOnlyList<ComponenteVista> Componentes, EvaluacionPc Evaluacion, IReadOnlyList<string> Avisos)
{
    public decimal Total => Componentes.Sum(c => c.Subtotal);
    public IEnumerable<ComponenteVista> De(TipoComponente t) => Componentes.Where(c => c.Datos.Tipo == t);
}

public record OpcionPc(ComponenteVista Componente, bool Elegida, Severidad Estado, IReadOnlyList<string> Motivos, decimal Diferencia, bool Sugerida, string UrlElegir);

public class ServicioPcBuilder
{
    private readonly AppDbContext _db;
    private readonly ServicioPrecios _precios;
    private readonly ServicioCarrito _carrito;
    private readonly TimeProvider _reloj;

    public ServicioPcBuilder(AppDbContext db, ServicioPrecios precios, ServicioCarrito carrito, TimeProvider reloj)
    {
        _db = db;
        _precios = precios;
        _carrito = carrito;
        _reloj = reloj;
    }

    private sealed record Fila(int Id, string Nombre, string Slug, string Marca, int MarcaId, int CategoriaId, TipoComponente Tipo,
        decimal Precio, decimal? Oferta, int Stock, bool Destacado, int Vendidos, string? Imagen, ProductoCompatibilidad? C);

    /// <summary>El filtro va antes de proyectar: EF no traduce condiciones sobre miembros de un record construido.</summary>
    private IQueryable<Fila> Consulta(System.Linq.Expressions.Expression<Func<Producto, bool>> filtro) => _db.Productos.AsNoTracking()
        .Where(p => p.Estado == EstadoProducto.Activo && p.Categoria.Activa && p.Categoria.TipoComponente != TipoComponente.Ninguno)
        .Where(filtro)
        .Select(p => new Fila(p.Id, p.Nombre, p.Slug, p.Marca.Nombre, p.MarcaId, p.CategoriaId, p.Categoria.TipoComponente,
            p.Precio, p.PrecioOferta, p.Stock, p.Destacado, p.TotalVendidos,
            p.Imagenes.OrderByDescending(i => i.EsPrincipal).ThenBy(i => i.Orden).Select(i => i.Url).FirstOrDefault(), p.Compatibilidad));

    private static ComponenteVista Vista(Fila f, int cantidad, IReadOnlyList<PromocionVigente> promos)
    {
        var c = f.C;
        var datos = new Componente(f.Id, f.Nombre, f.Tipo, cantidad, c?.Socket, c?.TiposMemoria, c?.FactorForma, c?.FactoresFormaSoportados,
            c?.SlotsMemoria, c?.MemoriaMaximaGb, c?.CapacidadGb, c?.Modulos, c?.ConsumoW, c?.PotenciaW, c?.FuenteRecomendadaW, c?.LargoMm,
            c?.LargoGpuMaximoMm, c?.AlturaMm, c?.AlturaCoolerMaximaMm, c?.SocketsSoportados, c?.Interfaz, c?.SlotsM2, c?.PuertosSata,
            c?.GraficosIntegrados, c?.IncluyeCooler);
        return new ComponenteVista(datos, f.Slug, f.Marca, f.Imagen,
            ServicioPrecios.Calcular(f.Precio, f.Oferta, f.Id, f.MarcaId, f.CategoriaId, promos), f.Stock, cantidad);
    }

    /// <summary>Carga y evalúa el armado. Descarta (con aviso) ids inexistentes, inactivos o de otro tipo de componente.</summary>
    public async Task<ArmadoPc> ArmarAsync(SeleccionPc seleccion, CancellationToken ct = default)
    {
        var ids = seleccion.Todos.Select(x => x.Id).Distinct().ToList();
        var filas = await Consulta(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var promos = await _precios.ObtenerPromocionesVigentesAsync(ct);
        var avisos = new List<string>();
        var limpia = new SeleccionPc { ConfiguracionId = seleccion.ConfiguracionId };
        var componentes = new List<ComponenteVista>();

        foreach (var (tipo, id, cant) in seleccion.Todos)
        {
            if (!filas.TryGetValue(id, out var f) || f.Tipo != tipo)
            {
                avisos.Add($"Un componente de {SeleccionPc.Paso(tipo).Nombre.ToLowerInvariant()} ya no está disponible y se quitó del armado.");
                continue;
            }
            limpia.Poner(tipo, id, cant, agregar: SeleccionPc.Paso(tipo).Multiple);
            componentes.Add(Vista(f, cant, promos));
        }

        return new ArmadoPc(limpia, componentes, ReglasCompatibilidad.Evaluar(componentes.Select(c => c.Datos).ToList()), avisos);
    }

    /// <summary>
    /// Opciones de un paso. Cada candidato se evalúa colocándolo en el armado actual: así se sabe si es compatible
    /// y por qué no. Sugerida = la mejor opción compatible con stock (destacada y más vendida, a igualdad la más barata).
    /// </summary>
    public async Task<IReadOnlyList<OpcionPc>> OpcionesAsync(ArmadoPc armado, PasoPc paso, CancellationToken ct = default)
    {
        var promos = await _precios.ObtenerPromocionesVigentesAsync(ct);
        var candidatos = await Consulta(p => p.Categoria.TipoComponente == paso.Tipo).ToListAsync(ct);
        var actuales = armado.De(paso.Tipo).ToList();
        var precioActual = paso.Multiple ? 0 : actuales.Sum(a => a.Subtotal);
        var resto = armado.Componentes.Where(c => c.Datos.Tipo != paso.Tipo || paso.Multiple).Select(c => c.Datos).ToList();

        var opciones = candidatos.Select(f =>
        {
            var elegida = actuales.Any(a => a.Datos.ProductoId == f.Id);
            var cantidad = elegida ? actuales.First(a => a.Datos.ProductoId == f.Id).Cantidad : 1;
            var vista = Vista(f, cantidad, promos);
            var prueba = resto.Where(r => r.ProductoId != f.Id).Append(vista.Datos).ToList();
            var problemas = ReglasCompatibilidad.Evaluar(prueba).Hallazgos
                .Where(h => h.Tipos.Contains(paso.Tipo) && h.Severidad is Severidad.Error or Severidad.Advertencia)
                .Where(h => h.Regla is not ("refrigeracion" or "video")) // dependen de otros pasos, no del candidato
                .ToList();
            var estado = problemas.Select(h => h.Severidad).DefaultIfEmpty(Severidad.Correcto).Max();
            var url = elegida
                ? armado.Seleccion.Clonar().Quitar(paso.Tipo, paso.Multiple ? f.Id : null).Query(paso.Clave)
                // Al elegir un componente único se avanza al siguiente paso pendiente (sin "paso" en la URL).
                : armado.Seleccion.Clonar().Poner(paso.Tipo, f.Id, 1, agregar: paso.Multiple).Query(paso.Multiple ? paso.Clave : null);
            return new OpcionPc(vista, elegida, estado, problemas.Select(h => h.Mensaje).ToList(),
                paso.Multiple ? vista.Precio.PrecioFinal : vista.Subtotal - precioActual, false, url);
        }).ToList();

        var sugerida = opciones.Where(o => o.Estado != Severidad.Error && o.Componente.Stock > 0)
            .OrderByDescending(o => candidatos.First(c => c.Id == o.Componente.Datos.ProductoId).Destacado)
            .ThenByDescending(o => candidatos.First(c => c.Id == o.Componente.Datos.ProductoId).Vendidos)
            .ThenBy(o => o.Componente.Precio.PrecioFinal)
            .FirstOrDefault();

        return opciones
            .Select(o => o == sugerida ? o with { Sugerida = true } : o)
            .OrderByDescending(o => o.Elegida)
            .ThenBy(o => o.Estado == Severidad.Error)
            .ThenByDescending(o => o.Sugerida)
            .ThenBy(o => o.Componente.Precio.PrecioFinal)
            .ToList();
    }

    // ---------- Configuraciones guardadas ----------

    public async Task<(int? Id, string? Error)> GuardarAsync(int usuarioId, SeleccionPc seleccion, string? nombre, CancellationToken ct = default)
    {
        nombre = (nombre ?? string.Empty).Trim();
        if (nombre.Length is < 2 or > 100) return (null, "Ponle un nombre de 2 a 100 caracteres a tu configuración.");

        var armado = await ArmarAsync(seleccion, ct);
        if (armado.Componentes.Count == 0) return (null, "Elige al menos un componente antes de guardar.");

        ConfiguracionPc? config = null;
        if (seleccion.ConfiguracionId is { } id)
            config = await _db.ConfiguracionesPc.Include(c => c.Items).FirstOrDefaultAsync(c => c.Id == id && c.UsuarioId == usuarioId, ct);
        var ahora = _reloj.GetUtcNow().UtcDateTime;
        if (config is null)
        {
            if (await _db.ConfiguracionesPc.CountAsync(c => c.UsuarioId == usuarioId, ct) >= 20) return (null, "Puedes guardar hasta 20 configuraciones.");
            config = new ConfiguracionPc { UsuarioId = usuarioId, FechaCreacion = ahora };
            _db.ConfiguracionesPc.Add(config);
        }
        else
        {
            _db.ConfiguracionPcItems.RemoveRange(config.Items);
            config.FechaActualizacion = ahora;
        }
        config.Nombre = nombre;
        config.Items = armado.Componentes.Select(c => new ConfiguracionPcItem { Tipo = c.Datos.Tipo, ProductoId = c.Datos.ProductoId, Cantidad = c.Cantidad }).ToList();
        await _db.SaveChangesAsync(ct);
        return (config.Id, null);
    }

    public record ResumenConfiguracion(int Id, string Nombre, DateTime Fecha, int Componentes, string Url);

    public async Task<List<ResumenConfiguracion>> ListarAsync(int usuarioId, CancellationToken ct = default)
    {
        var configs = await _db.ConfiguracionesPc.AsNoTracking().Include(c => c.Items)
            .Where(c => c.UsuarioId == usuarioId).OrderByDescending(c => c.FechaActualizacion ?? c.FechaCreacion).ToListAsync(ct);
        return configs.Select(c => new ResumenConfiguracion(c.Id, c.Nombre, c.FechaActualizacion ?? c.FechaCreacion, c.Items.Count, Seleccion(c).Query())).ToList();
    }

    /// <summary>Selección de una configuración propia (null si no existe o es de otro usuario).</summary>
    public async Task<(SeleccionPc Seleccion, string Nombre)?> AbrirAsync(int usuarioId, int id, CancellationToken ct = default)
    {
        var c = await _db.ConfiguracionesPc.AsNoTracking().Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id && x.UsuarioId == usuarioId, ct);
        return c is null ? null : (Seleccion(c), c.Nombre);
    }

    public async Task<bool> EliminarAsync(int usuarioId, int id, CancellationToken ct = default) =>
        await _db.ConfiguracionesPc.Where(c => c.Id == id && c.UsuarioId == usuarioId).ExecuteDeleteAsync(ct) > 0;

    private static SeleccionPc Seleccion(ConfiguracionPc c)
    {
        var s = new SeleccionPc { ConfiguracionId = c.Id };
        foreach (var i in c.Items) s.Poner(i.Tipo, i.ProductoId, i.Cantidad, agregar: SeleccionPc.Paso(i.Tipo).Multiple);
        return s;
    }

    /// <summary>Agrega todo el armado al carrito. Devuelve los mensajes de lo que no se pudo agregar completo.</summary>
    public async Task<(int Agregados, List<string> Problemas)> AgregarAlCarritoAsync(DuenoCarrito dueno, SeleccionPc seleccion, CancellationToken ct = default)
    {
        var armado = await ArmarAsync(seleccion, ct);
        var problemas = new List<string>();
        var agregados = 0;
        foreach (var c in armado.Componentes)
        {
            var r = await _carrito.AgregarAsync(dueno, c.Datos.ProductoId, c.Cantidad, ct);
            // "Ajustado" sí entra al carrito, pero con menos unidades: se cuenta y se avisa.
            if (r.Estado is ViewModels.EstadoAgregar.Agregado or ViewModels.EstadoAgregar.Ajustado) agregados++;
            if (r.Estado != ViewModels.EstadoAgregar.Agregado) problemas.Add(r.Mensaje);
        }
        return (agregados, problemas);
    }
}
