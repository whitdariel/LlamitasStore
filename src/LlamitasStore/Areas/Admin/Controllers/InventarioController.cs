using LlamitasStore.Areas.Admin.Models;
using LlamitasStore.Data;
using LlamitasStore.Infraestructura;
using LlamitasStore.Models.Entities;
using LlamitasStore.Services.Admin;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace LlamitasStore.Areas.Admin.Controllers;

[Route("admin/inventario")]
public class InventarioController : AdminController
{
    private readonly AppDbContext _db;
    private readonly ServicioInventario _inventario;

    public InventarioController(AppDbContext db, ServicioInventario inventario)
    {
        _db = db;
        _inventario = inventario;
    }

    public record FilaStock(int Id, string Sku, string Nombre, string Categoria, int Stock, int Minimo, EstadoProducto Estado);
    public record FilaMovimiento(DateTime Fecha, string Producto, string Sku, TipoMovimiento Tipo, int Cantidad, int Resultante, string Motivo, string? Usuario);
    public record FilaPrecio(DateTime Fecha, string Producto, decimal Anterior, decimal Nuevo, decimal? OfertaAnterior, decimal? OfertaNueva, string? Usuario);

    [HttpGet("")]
    [Permiso(Modulos.Inventario)]
    public async Task<IActionResult> Index(string? q, bool soloBajo = false, int pagina = 1, CancellationToken ct = default)
    {
        var consulta = _db.Productos.AsNoTracking().Where(p => p.Estado != EstadoProducto.Inactivo);
        if (!string.IsNullOrWhiteSpace(q)) consulta = consulta.Where(p => EF.Functions.Like(p.Nombre, $"%{q.Trim()}%") || EF.Functions.Like(p.Sku, $"%{q.Trim()}%"));
        if (soloBajo) consulta = consulta.Where(p => p.Stock <= p.StockMinimo);

        var (actual, saltar) = Paginar(pagina);
        var total = await consulta.CountAsync(ct);
        var filas = await consulta.OrderBy(p => p.Stock > p.StockMinimo).ThenBy(p => p.Stock).ThenBy(p => p.Nombre)
            .Skip(saltar).Take(TamanoPagina)
            .Select(p => new FilaStock(p.Id, p.Sku, p.Nombre, p.Categoria.Nombre, p.Stock, p.StockMinimo, p.Estado))
            .ToListAsync(ct);
        (ViewData["Q"], ViewData["SoloBajo"]) = (q, soloBajo);
        return View(new Pagina<FilaStock>(filas, total, actual));
    }

    [HttpPost("ajustar")]
    [Permiso(Modulos.Inventario, P.Editar)]
    public async Task<IActionResult> Ajustar(int productoId, TipoMovimiento tipo, int cantidad, string? motivo, string? returnUrl, CancellationToken ct)
    {
        var error = await _inventario.AjustarAsync(productoId, tipo, cantidad, motivo ?? string.Empty, UsuarioId, ct);
        TempData[error is null ? "Exito" : "Error"] = error ?? "Movimiento registrado.";
        return Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl) : RedirectToAction(nameof(Index));
    }

    [HttpGet("movimientos")]
    [Permiso(Modulos.Inventario)]
    public async Task<IActionResult> Movimientos(int? producto, int pagina = 1, CancellationToken ct = default)
    {
        var consulta = _db.MovimientosInventario.AsNoTracking();
        if (producto is > 0) consulta = consulta.Where(m => m.ProductoId == producto);
        var (actual, saltar) = Paginar(pagina);
        var total = await consulta.CountAsync(ct);
        var filas = await consulta.OrderByDescending(m => m.Fecha).ThenByDescending(m => m.Id).Skip(saltar).Take(TamanoPagina)
            .Select(m => new FilaMovimiento(m.Fecha, m.Producto.Nombre, m.Producto.Sku, m.Tipo, m.Cantidad, m.StockResultante, m.Motivo,
                m.Usuario != null ? m.Usuario.Nombre + " " + m.Usuario.Apellido : null))
            .ToListAsync(ct);
        ViewData["Producto"] = producto is > 0 ? await _db.Productos.Where(p => p.Id == producto).Select(p => p.Nombre).FirstOrDefaultAsync(ct) : null;
        ViewData["ProductoId"] = producto;
        return View(new Pagina<FilaMovimiento>(filas, total, actual));
    }

    [HttpGet("precios")]
    [Permiso(Modulos.Inventario)]
    public async Task<IActionResult> Precios(int pagina = 1, CancellationToken ct = default)
    {
        var (actual, saltar) = Paginar(pagina);
        var total = await _db.HistorialPrecios.CountAsync(ct);
        var filas = await _db.HistorialPrecios.AsNoTracking().OrderByDescending(h => h.Fecha).Skip(saltar).Take(TamanoPagina)
            .Select(h => new FilaPrecio(h.Fecha, h.Producto.Nombre, h.PrecioAnterior, h.PrecioNuevo, h.PrecioOfertaAnterior, h.PrecioOfertaNuevo,
                h.Usuario != null ? h.Usuario.Nombre + " " + h.Usuario.Apellido : null))
            .ToListAsync(ct);
        return View(new Pagina<FilaPrecio>(filas, total, actual));
    }
}

[Route("admin/proveedores")]
public class ProveedoresController : AdminController
{
    private readonly AppDbContext _db;
    private readonly ServicioInventario _inventario;

    public ProveedoresController(AppDbContext db, ServicioInventario inventario)
    {
        _db = db;
        _inventario = inventario;
    }

    [HttpGet("")]
    [Permiso(Modulos.Proveedores)]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        ViewData["Ordenes"] = await _db.OrdenesCompra.AsNoTracking().Include(o => o.Proveedor)
            .OrderByDescending(o => o.Fecha).Take(30).ToListAsync(ct);
        return View(await _db.Proveedores.AsNoTracking().OrderBy(p => p.Nombre).ToListAsync(ct));
    }

    [HttpGet("nuevo")]
    [Permiso(Modulos.Proveedores, P.Crear)]
    public IActionResult Nuevo() => View("Formulario", new ProveedorFormVm());

    [HttpGet("{id:int}")]
    [Permiso(Modulos.Proveedores, P.Editar)]
    public async Task<IActionResult> Editar(int id, CancellationToken ct)
    {
        var p = await _db.Proveedores.FindAsync([id], ct);
        return p is null ? NotFound() : View("Formulario", new ProveedorFormVm
        { Id = p.Id, Nombre = p.Nombre, Nit = p.Nit, Telefono = p.Telefono, Email = p.Email, Direccion = p.Direccion, Activo = p.Activo });
    }

    [HttpPost("nuevo")]
    [Permiso(Modulos.Proveedores, P.Crear)]
    public Task<IActionResult> Nuevo(ProveedorFormVm vm, CancellationToken ct) { vm.Id = null; return GuardarAsync(vm, ct); }

    [HttpPost("{id:int}")]
    [Permiso(Modulos.Proveedores, P.Editar)]
    public Task<IActionResult> Editar(int id, ProveedorFormVm vm, CancellationToken ct) { vm.Id = id; return GuardarAsync(vm, ct); }

    private async Task<IActionResult> GuardarAsync(ProveedorFormVm vm, CancellationToken ct)
    {
        if (await _db.Proveedores.AnyAsync(p => p.Nombre == vm.Nombre.Trim() && p.Id != vm.Id, ct))
            ModelState.AddModelError(nameof(vm.Nombre), "Ya existe un proveedor con ese nombre.");
        if (!ModelState.IsValid) return View("Formulario", vm);
        var p = vm.Id is null ? _db.Proveedores.Add(new Proveedor()).Entity : await _db.Proveedores.FindAsync([vm.Id.Value], ct);
        if (p is null) return NotFound();
        (p.Nombre, p.Nit, p.Telefono, p.Email, p.Direccion, p.Activo) = (vm.Nombre.Trim(), vm.Nit?.Trim(), vm.Telefono?.Trim(), vm.Email?.Trim(), vm.Direccion?.Trim(), vm.Activo);
        await _db.SaveChangesAsync(ct);
        return Resultado(null, "Proveedor guardado.", nameof(Index));
    }

    // ---------- Órdenes de compra ----------

    [HttpGet("ordenes/nueva")]
    [Permiso(Modulos.Proveedores, P.Crear)]
    public async Task<IActionResult> NuevaOrden(CancellationToken ct)
    {
        await ListasOrdenAsync(ct);
        return View();
    }

    [HttpPost("ordenes/nueva")]
    [Permiso(Modulos.Proveedores, P.Crear)]
    public async Task<IActionResult> NuevaOrden(int proveedorId, int[] productoId, int[] cantidad, decimal[] costo, string? notas, CancellationToken ct)
    {
        var lineas = productoId.Select((id, i) => new LineaOrdenCompra(id, i < cantidad.Length ? cantidad[i] : 0, i < costo.Length ? costo[i] : 0)).ToList();
        var (orden, error) = await _inventario.CrearOrdenCompraAsync(proveedorId, lineas, notas, UsuarioId, ct);
        if (orden is null)
        {
            ModelState.AddModelError(string.Empty, error!);
            await ListasOrdenAsync(ct);
            return View();
        }
        return Resultado(null, $"Orden {orden.Numero} creada en borrador. Recíbela cuando llegue la mercadería.", nameof(Orden), new { id = orden.Id });
    }

    [HttpGet("ordenes/{id:int}")]
    [Permiso(Modulos.Proveedores)]
    public async Task<IActionResult> Orden(int id, CancellationToken ct)
    {
        var o = await _db.OrdenesCompra.AsNoTracking().Include(x => x.Proveedor).Include(x => x.Usuario)
            .Include(x => x.Detalles).ThenInclude(d => d.Producto).FirstOrDefaultAsync(x => x.Id == id, ct);
        return o is null ? NotFound() : View(o);
    }

    [HttpPost("ordenes/{id:int}/recibir")]
    [Permiso(Modulos.Proveedores, P.Editar)]
    public async Task<IActionResult> RecibirOrden(int id, CancellationToken ct) =>
        Resultado(await _inventario.RecibirOrdenCompraAsync(id, UsuarioId, ct), "Mercadería recibida: el stock se actualizó.", nameof(Orden), new { id });

    [HttpPost("ordenes/{id:int}/cancelar")]
    [Permiso(Modulos.Proveedores, P.Editar)]
    public async Task<IActionResult> CancelarOrden(int id, CancellationToken ct) =>
        Resultado(await _inventario.CancelarOrdenCompraAsync(id, ct), "Orden cancelada.", nameof(Orden), new { id });

    private async Task ListasOrdenAsync(CancellationToken ct)
    {
        ViewData["Proveedores"] = new SelectList(await _db.Proveedores.Where(p => p.Activo).OrderBy(p => p.Nombre).ToListAsync(ct), "Id", "Nombre");
        ViewData["Productos"] = await _db.Productos.Where(p => p.Estado != EstadoProducto.Inactivo).OrderBy(p => p.Nombre)
            .Select(p => new SelectListItem($"{p.Nombre} ({p.Sku})", p.Id.ToString())).ToListAsync(ct);
    }
}
