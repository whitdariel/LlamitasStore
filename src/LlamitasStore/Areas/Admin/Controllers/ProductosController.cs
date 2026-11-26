using LlamitasStore.Areas.Admin.Models;
using LlamitasStore.Data;
using LlamitasStore.Infraestructura;
using LlamitasStore.Models.Entities;
using LlamitasStore.Services.Admin;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace LlamitasStore.Areas.Admin.Controllers;

[Route("admin/productos")]
public class ProductosController : AdminController
{
    private readonly AppDbContext _db;
    private readonly ServicioAdminProductos _productos;
    private readonly Microsoft.Extensions.Caching.Memory.IMemoryCache _cache;

    public ProductosController(AppDbContext db, ServicioAdminProductos productos, Microsoft.Extensions.Caching.Memory.IMemoryCache cache)
    {
        _db = db;
        _productos = productos;
        _cache = cache;
    }

    /// <summary>El menú de categorías muestra cuántos productos activos hay: se recalcula al cambiar productos.</summary>
    private void RefrescarMenu() => _cache.Remove(ViewComponents.CategoriasViewComponent.ClaveCache);

    public record FilaProducto(int Id, string Sku, string Nombre, string Slug, string Categoria, string Marca, decimal Precio, decimal? Oferta, int Stock, int Minimo, EstadoProducto Estado, string? Imagen);

    [HttpGet("")]
    [Permiso(Modulos.Productos)]
    public async Task<IActionResult> Index(string? q, int? categoria, EstadoProducto? estado, bool stockBajo = false, int pagina = 1, CancellationToken ct = default)
    {
        var consulta = _db.Productos.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var patron = $"%{q.Trim()}%";
            consulta = consulta.Where(p => EF.Functions.Like(p.Nombre, patron) || EF.Functions.Like(p.Sku, patron));
        }
        if (categoria is > 0) consulta = consulta.Where(p => p.CategoriaId == categoria);
        if (estado is not null) consulta = consulta.Where(p => p.Estado == estado);
        if (stockBajo) consulta = consulta.Where(p => p.Estado == EstadoProducto.Activo && p.Stock <= p.StockMinimo);

        var (actual, saltar) = Paginar(pagina);
        var total = await consulta.CountAsync(ct);
        var filas = await consulta.OrderBy(p => p.Nombre).Skip(saltar).Take(TamanoPagina)
            .Select(p => new FilaProducto(p.Id, p.Sku, p.Nombre, p.Slug, p.Categoria.Nombre, p.Marca.Nombre, p.Precio, p.PrecioOferta, p.Stock, p.StockMinimo, p.Estado,
                p.Imagenes.OrderByDescending(i => i.EsPrincipal).Select(i => i.Url).FirstOrDefault()))
            .ToListAsync(ct);

        ViewData["Categorias"] = new SelectList(await _db.Categorias.OrderBy(c => c.Orden).ToListAsync(ct), "Id", "Nombre", categoria);
        (ViewData["Q"], ViewData["Estado"], ViewData["StockBajo"]) = (q, estado, stockBajo);
        return View(new Pagina<FilaProducto>(filas, total, actual));
    }

    [HttpGet("nuevo")]
    [Permiso(Modulos.Productos, P.Crear)]
    public async Task<IActionResult> Nuevo() { await ListasAsync(); return View("Formulario", new ProductoFormVm()); }

    [HttpPost("nuevo")]
    [Permiso(Modulos.Productos, P.Crear)]
    public Task<IActionResult> Nuevo(ProductoFormVm vm, CancellationToken ct) { vm.Id = null; return GuardarAsync(vm, ct); }

    [HttpGet("{id:int}")]
    [Permiso(Modulos.Productos, P.Editar)]
    public async Task<IActionResult> Editar(int id, CancellationToken ct)
    {
        var p = await _productos.ObtenerAsync(id, ct);
        if (p is null) return NotFound();
        await ListasAsync(p);
        return View("Formulario", ProductoFormVm.Desde(p));
    }

    [HttpPost("{id:int}")]
    [Permiso(Modulos.Productos, P.Editar)]
    public async Task<IActionResult> Editar(int id, ProductoFormVm vm, CancellationToken ct)
    {
        if (!await _db.Productos.AnyAsync(p => p.Id == id, ct)) return NotFound();
        vm.Id = id;
        return await GuardarAsync(vm, ct);
    }

    private async Task<IActionResult> GuardarAsync(ProductoFormVm vm, CancellationToken ct)
    {
        if (ModelState.IsValid)
        {
            var (id, errores) = await _productos.GuardarAsync(vm, UsuarioId, ct);
            foreach (var (campo, msg) in errores) ModelState.AddModelError(campo, msg);
            if (id is not null)
            {
                RefrescarMenu();
                TempData["Exito"] = vm.Id is null ? "Producto creado. Ahora puedes subir sus imágenes." : "Producto actualizado.";
                return RedirectToAction(nameof(Editar), new { id });
            }
        }
        await ListasAsync(vm.Id is null ? null : await _productos.ObtenerAsync(vm.Id.Value, ct));
        return View("Formulario", vm);
    }

    [HttpPost("{id:int}/imagenes")]
    [Permiso(Modulos.Productos, P.Editar)]
    [RequestSizeLimit(ServicioAdminProductos.TamanoMaximoImagen + 64 * 1024)]
    public async Task<IActionResult> SubirImagen(int id, IFormFile? archivo, string? textoAlternativo, CancellationToken ct) =>
        Resultado(await _productos.SubirImagenAsync(id, archivo, textoAlternativo, ct), "Imagen subida.", nameof(Editar), new { id });

    [HttpPost("{id:int}/imagenes/{imagenId:int}/principal")]
    [Permiso(Modulos.Productos, P.Editar)]
    public async Task<IActionResult> ImagenPrincipal(int id, int imagenId, CancellationToken ct) =>
        Resultado(await _productos.MarcarImagenPrincipalAsync(id, imagenId, ct), "Imagen principal actualizada.", nameof(Editar), new { id });

    [HttpPost("{id:int}/imagenes/{imagenId:int}/eliminar")]
    [Permiso(Modulos.Productos, P.Editar)]
    public async Task<IActionResult> EliminarImagen(int id, int imagenId, CancellationToken ct) =>
        Resultado(await _productos.EliminarImagenAsync(id, imagenId, ct), "Imagen eliminada.", nameof(Editar), new { id });

    /// <summary>"Eliminar" un producto lo da de baja (Inactivo): sus pedidos históricos deben seguir apuntando a él.</summary>
    [HttpPost("{id:int}/baja")]
    [Permiso(Modulos.Productos, P.Eliminar)]
    public async Task<IActionResult> DarDeBaja(int id, CancellationToken ct)
    {
        var filas = await _db.Productos.Where(p => p.Id == id).ExecuteUpdateAsync(s => s.SetProperty(p => p.Estado, EstadoProducto.Inactivo), ct);
        RefrescarMenu();
        return Resultado(filas == 0 ? "El producto no existe." : null, "Producto dado de baja: ya no aparece en la tienda.", nameof(Index));
    }

    private async Task ListasAsync(Producto? p = null)
    {
        ViewData["Categorias"] = new SelectList(await _db.Categorias.OrderBy(c => c.Orden).ToListAsync(), "Id", "Nombre");
        ViewData["Marcas"] = new SelectList(await _db.Marcas.OrderBy(m => m.Nombre).ToListAsync(), "Id", "Nombre");
        ViewData["Producto"] = p;
    }
}
