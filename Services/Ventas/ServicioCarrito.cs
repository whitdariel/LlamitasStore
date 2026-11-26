using System.Security.Claims;
using LlamitasStore.Data;
using LlamitasStore.Models.Entities;
using LlamitasStore.Services.Catalogo;
using LlamitasStore.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace LlamitasStore.Services.Ventas;

/// <summary>Dueño de un carrito: un usuario registrado o un visitante identificado por el token de su cookie.</summary>
public record DuenoCarrito(int? UsuarioId, string? Token)
{
    public bool Valido => UsuarioId is not null || !string.IsNullOrEmpty(Token);
}

/// <summary>
/// Carrito persistente en base de datos. Reglas:
/// - Solo productos activos con stock; la cantidad se limita al stock y a <see cref="MaximoPorProducto"/>.
/// - Los precios NO se guardan en el carrito: siempre se muestran los vigentes (ofertas y promociones de hoy).
/// - El stock real se descuenta recién al confirmar el pedido (ver ServicioPedidos).
/// </summary>
public class ServicioCarrito
{
    public const int MaximoPorProducto = 10;

    private readonly AppDbContext _db;
    private readonly ServicioPrecios _precios;
    private readonly ServicioCupones _cupones;
    private readonly AjustesTienda _ajustes;

    public ServicioCarrito(AppDbContext db, ServicioPrecios precios, ServicioCupones cupones, AjustesTienda ajustes)
    {
        _db = db;
        _precios = precios;
        _cupones = cupones;
        _ajustes = ajustes;
    }

    private IQueryable<Carrito> De(DuenoCarrito d) => d.UsuarioId is not null
        ? _db.Carritos.Where(c => c.UsuarioId == d.UsuarioId)
        : _db.Carritos.Where(c => c.TokenAnonimo == d.Token && c.UsuarioId == null);

    public async Task<int> ContarAsync(DuenoCarrito d, CancellationToken ct = default) =>
        d.Valido ? await De(d).SelectMany(c => c.Items).SumAsync(i => (int?)i.Cantidad, ct) ?? 0 : 0;

    public async Task<CarritoVista> ObtenerAsync(DuenoCarrito d, CancellationToken ct = default)
    {
        var envioGratis = _ajustes.ObtenerDecimal(ClavesAjuste.EnvioGratisDesde);
        if (!d.Valido) return CarritoVista.Vacio(envioGratis);

        var carrito = await De(d).AsNoTracking()
            .Select(c => new
            {
                c.CodigoCupon,
                Items = c.Items.OrderBy(i => i.FechaAgregado).Select(i => new
                {
                    i.ProductoId, i.Cantidad, i.Producto.Nombre, i.Producto.Slug, i.Producto.Sku, Marca = i.Producto.Marca.Nombre,
                    i.Producto.Precio, i.Producto.PrecioOferta, i.Producto.MarcaId, i.Producto.CategoriaId, i.Producto.Stock,
                    Activo = i.Producto.Estado == EstadoProducto.Activo,
                    Imagen = i.Producto.Imagenes.OrderByDescending(x => x.EsPrincipal).ThenBy(x => x.Orden).Select(x => x.Url).FirstOrDefault()
                }).ToList()
            })
            .FirstOrDefaultAsync(ct);
        if (carrito is null || carrito.Items.Count == 0) return CarritoVista.Vacio(envioGratis);

        var promociones = await _precios.ObtenerPromocionesVigentesAsync(ct);
        var items = carrito.Items.Select(i => new ItemCarritoVista(i.ProductoId, i.Nombre, i.Slug, i.Sku, i.Marca, i.Imagen,
            ServicioPrecios.Calcular(i.Precio, i.PrecioOferta, i.ProductoId, i.MarcaId, i.CategoriaId, promociones),
            i.Cantidad, i.Stock, i.Activo)).ToList();

        // El cupón se valida contra el subtotal de los productos que realmente se pueden comprar.
        var subtotal = items.Where(i => i.Comprable).Sum(i => i.Subtotal);
        CuponAplicable? cupon = null;
        string? errorCupon = null;
        if (!string.IsNullOrEmpty(carrito.CodigoCupon))
        {
            var v = await _cupones.ValidarAsync(carrito.CodigoCupon, d.UsuarioId, subtotal, ct);
            (cupon, errorCupon) = (v.Cupon, v.Error);
        }

        var totales = CalculadoraTotales.Calcular(
            items.Where(i => i.Comprable).Select(i => new LineaCalculo(i.Precio.PrecioFinal, i.Cantidad)),
            cupon, _ajustes.ObtenerDecimal(ClavesAjuste.CostoEnvio), envioGratis);

        return new CarritoVista(items, totales, carrito.CodigoCupon, cupon, errorCupon);
    }

    public async Task<ResultadoCarrito> AgregarAsync(DuenoCarrito d, int productoId, int cantidad, CancellationToken ct = default)
    {
        cantidad = Math.Clamp(cantidad, 1, MaximoPorProducto);
        var producto = await _db.Productos.AsNoTracking()
            .Where(p => p.Id == productoId)
            .Select(p => new { p.Nombre, p.Stock, Activo = p.Estado == EstadoProducto.Activo })
            .FirstOrDefaultAsync(ct);

        if (producto is null || !producto.Activo)
            return new(EstadoAgregar.NoDisponible, 0, await ContarAsync(d, ct), "Este producto ya no está disponible.");
        if (producto.Stock <= 0)
            return new(EstadoAgregar.SinStock, 0, await ContarAsync(d, ct), $"{producto.Nombre} está agotado.");

        var carrito = await ObtenerOCrearAsync(d, ct);
        var item = carrito.Items.FirstOrDefault(i => i.ProductoId == productoId);
        var deseada = (item?.Cantidad ?? 0) + cantidad;
        var limite = Math.Min(producto.Stock, MaximoPorProducto);
        var final = Math.Min(deseada, limite);

        if (item is null) carrito.Items.Add(new CarritoItem { ProductoId = productoId, Cantidad = final });
        else item.Cantidad = final;
        carrito.FechaActualizacion = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        var total = await ContarAsync(d, ct);
        return final < deseada
            ? new(EstadoAgregar.Ajustado, final, total, $"Tienes {final} unidad(es) de {producto.Nombre}: es el máximo disponible por pedido.")
            : new(EstadoAgregar.Agregado, final, total, $"{producto.Nombre} se agregó a tu carrito.");
    }

    /// <summary>Cambia la cantidad (0 = quitar). Devuelve la cantidad final.</summary>
    public async Task<int> ActualizarAsync(DuenoCarrito d, int productoId, int cantidad, CancellationToken ct = default)
    {
        var item = await De(d).SelectMany(c => c.Items).FirstOrDefaultAsync(i => i.ProductoId == productoId, ct);
        if (item is null) return 0;

        if (cantidad <= 0)
        {
            _db.CarritoItems.Remove(item);
            await _db.SaveChangesAsync(ct);
            return 0;
        }

        var stock = await _db.Productos.Where(p => p.Id == productoId).Select(p => p.Stock).FirstAsync(ct);
        item.Cantidad = Math.Max(1, Math.Min(cantidad, Math.Min(Math.Max(stock, 1), MaximoPorProducto)));
        await _db.SaveChangesAsync(ct);
        return item.Cantidad;
    }

    public Task<int> QuitarAsync(DuenoCarrito d, int productoId, CancellationToken ct = default) => ActualizarAsync(d, productoId, 0, ct);

    public async Task<ValidacionCupon> AplicarCuponAsync(DuenoCarrito d, string? codigo, CancellationToken ct = default)
    {
        var vista = await ObtenerAsync(d, ct);
        if (vista.EstaVacio) return new(null, "Agrega productos antes de usar un cupón.");

        var v = await _cupones.ValidarAsync(codigo, d.UsuarioId, vista.Totales.Subtotal, ct);
        if (!v.Valido) return v;

        var carrito = await De(d).FirstAsync(ct);
        carrito.CodigoCupon = v.Cupon!.Codigo;
        await _db.SaveChangesAsync(ct);
        return v;
    }

    public async Task QuitarCuponAsync(DuenoCarrito d, CancellationToken ct = default) =>
        await De(d).ExecuteUpdateAsync(s => s.SetProperty(c => c.CodigoCupon, (string?)null), ct);

    public async Task VaciarAsync(DuenoCarrito d, CancellationToken ct = default)
    {
        var ids = De(d).Select(c => c.Id);
        await _db.CarritoItems.Where(i => ids.Contains(i.CarritoId)).ExecuteDeleteAsync(ct);
        await QuitarCuponAsync(d, ct);
    }

    /// <summary>
    /// Al iniciar sesión, lo que el visitante tenía en su carrito pasa al carrito del usuario
    /// (las cantidades se suman respetando stock y máximo) y el carrito anónimo se elimina.
    /// </summary>
    public async Task FusionarAsync(string token, int usuarioId, CancellationToken ct = default)
    {
        var anonimo = await _db.Carritos.Include(c => c.Items)
            .FirstOrDefaultAsync(c => c.TokenAnonimo == token && c.UsuarioId == null, ct);
        if (anonimo is null) return;

        if (anonimo.Items.Count > 0)
        {
            var propio = await ObtenerOCrearAsync(new DuenoCarrito(usuarioId, null), ct);
            var stocks = await _db.Productos.Where(p => anonimo.Items.Select(i => i.ProductoId).Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.Stock, ct);

            foreach (var item in anonimo.Items)
            {
                var limite = Math.Min(stocks.GetValueOrDefault(item.ProductoId), MaximoPorProducto);
                if (limite <= 0) continue;
                var existente = propio.Items.FirstOrDefault(i => i.ProductoId == item.ProductoId);
                if (existente is null) propio.Items.Add(new CarritoItem { ProductoId = item.ProductoId, Cantidad = Math.Min(item.Cantidad, limite) });
                else existente.Cantidad = Math.Min(existente.Cantidad + item.Cantidad, limite);
            }
            propio.CodigoCupon ??= anonimo.CodigoCupon;
            propio.FechaActualizacion = DateTime.UtcNow;
        }

        _db.Carritos.Remove(anonimo);
        await _db.SaveChangesAsync(ct);
    }

    private async Task<Carrito> ObtenerOCrearAsync(DuenoCarrito d, CancellationToken ct)
    {
        var carrito = await De(d).Include(c => c.Items).FirstOrDefaultAsync(ct);
        if (carrito is not null) return carrito;

        carrito = new Carrito { UsuarioId = d.UsuarioId, TokenAnonimo = d.UsuarioId is null ? d.Token : null };
        _db.Carritos.Add(carrito);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Otra petición simultánea creó el carrito (índice único): se usa ese.
            _db.Entry(carrito).State = EntityState.Detached;
            carrito = await De(d).Include(c => c.Items).FirstAsync(ct);
        }
        return carrito;
    }
}

/// <summary>Resuelve el dueño del carrito para la petición actual (usuario o cookie del visitante).</summary>
public class CarritoActual
{
    public const string Cookie = ".LlamitasStore.Carrito";
    private readonly IHttpContextAccessor _http;

    public CarritoActual(IHttpContextAccessor http) => _http = http;

    /// <param name="crearToken">true al agregar productos: crea la cookie del visitante si no existe.</param>
    public DuenoCarrito Dueno(bool crearToken = false)
    {
        var ctx = _http.HttpContext!;
        if (int.TryParse(ctx.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)) return new DuenoCarrito(id, null);

        var token = ctx.Request.Cookies[Cookie] ?? ctx.Items[Cookie] as string;
        if (token is not null && Guid.TryParse(token, out _)) return new DuenoCarrito(null, token);
        if (!crearToken) return new DuenoCarrito(null, null);

        token = Guid.NewGuid().ToString();
        ctx.Response.Cookies.Append(Cookie, token, OpcionesCookie(ctx));
        // Disponible en esta misma petición.
        ctx.Items[Cookie] = token;
        return new DuenoCarrito(null, token);
    }

    public string? TokenVisitante()
    {
        var ctx = _http.HttpContext!;
        var token = ctx.Request.Cookies[Cookie] ?? ctx.Items[Cookie] as string;
        return token is not null && Guid.TryParse(token, out _) ? token : null;
    }

    public void BorrarCookie()
    {
        var ctx = _http.HttpContext!;
        ctx.Response.Cookies.Delete(Cookie, OpcionesCookie(ctx));
    }

    private static CookieOptions OpcionesCookie(HttpContext ctx) => new()
    {
        HttpOnly = true,
        IsEssential = true,
        SameSite = SameSiteMode.Lax,
        Secure = ctx.Request.IsHttps,
        Expires = DateTimeOffset.UtcNow.AddDays(30)
    };
}
