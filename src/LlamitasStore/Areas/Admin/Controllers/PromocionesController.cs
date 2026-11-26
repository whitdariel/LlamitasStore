using LlamitasStore.Areas.Admin.Models;
using LlamitasStore.Data;
using LlamitasStore.Infraestructura;
using LlamitasStore.Models.Entities;
using LlamitasStore.Services.Ventas;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace LlamitasStore.Areas.Admin.Controllers;

/// <summary>Promociones (descuento automático por categoría/marca/producto) y cupones (código que escribe el cliente).</summary>
[Route("admin/promociones")]
public class PromocionesController : AdminController
{
    private readonly AppDbContext _db;
    public PromocionesController(AppDbContext db) => _db = db;

    [HttpGet("")]
    [Permiso(Modulos.Promociones)]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        ViewData["Cupones"] = await _db.Cupones.AsNoTracking().OrderByDescending(c => c.FechaFin).ToListAsync(ct);
        return View(await _db.Promociones.AsNoTracking().Include(p => p.Categoria).Include(p => p.Marca).Include(p => p.Producto)
            .OrderByDescending(p => p.FechaFin).ToListAsync(ct));
    }

    // ---------- Promociones ----------

    [HttpGet("nueva")]
    [Permiso(Modulos.Promociones, P.Crear)]
    public async Task<IActionResult> Nueva(CancellationToken ct) { await ListasAsync(ct); return View("Promocion", new PromocionFormVm()); }

    [HttpGet("{id:int}")]
    [Permiso(Modulos.Promociones, P.Editar)]
    public async Task<IActionResult> Editar(int id, CancellationToken ct)
    {
        var p = await _db.Promociones.FindAsync([id], ct);
        if (p is null) return NotFound();
        await ListasAsync(ct);
        return View("Promocion", new PromocionFormVm
        {
            Id = p.Id, Nombre = p.Nombre, Descripcion = p.Descripcion, Porcentaje = p.Porcentaje, FechaInicio = p.FechaInicio, FechaFin = p.FechaFin,
            Activa = p.Activa, CategoriaId = p.CategoriaId, MarcaId = p.MarcaId, ProductoId = p.ProductoId
        });
    }

    [HttpPost("nueva")]
    [Permiso(Modulos.Promociones, P.Crear)]
    public Task<IActionResult> Nueva(PromocionFormVm vm, CancellationToken ct) { vm.Id = null; return GuardarAsync(vm, ct); }

    [HttpPost("{id:int}")]
    [Permiso(Modulos.Promociones, P.Editar)]
    public Task<IActionResult> Editar(int id, PromocionFormVm vm, CancellationToken ct) { vm.Id = id; return GuardarAsync(vm, ct); }

    private async Task<IActionResult> GuardarAsync(PromocionFormVm vm, CancellationToken ct)
    {
        if (!ModelState.IsValid) { await ListasAsync(ct); return View("Promocion", vm); }
        var p = vm.Id is null ? _db.Promociones.Add(new Promocion()).Entity : await _db.Promociones.FindAsync([vm.Id.Value], ct);
        if (p is null) return NotFound();
        (p.Nombre, p.Descripcion, p.Porcentaje, p.FechaInicio, p.FechaFin, p.Activa) =
            (vm.Nombre.Trim(), vm.Descripcion?.Trim(), vm.Porcentaje, DateTime.SpecifyKind(vm.FechaInicio, DateTimeKind.Utc), DateTime.SpecifyKind(vm.FechaFin, DateTimeKind.Utc), vm.Activa);
        (p.CategoriaId, p.MarcaId, p.ProductoId) = (vm.CategoriaId is > 0 ? vm.CategoriaId : null, vm.MarcaId is > 0 ? vm.MarcaId : null, vm.ProductoId is > 0 ? vm.ProductoId : null);
        await _db.SaveChangesAsync(ct);
        return Resultado(null, "Promoción guardada.", nameof(Index));
    }

    [HttpPost("{id:int}/eliminar")]
    [Permiso(Modulos.Promociones, P.Eliminar)]
    public async Task<IActionResult> Eliminar(int id, CancellationToken ct)
    {
        await _db.Promociones.Where(p => p.Id == id).ExecuteDeleteAsync(ct);
        return Resultado(null, "Promoción eliminada.", nameof(Index));
    }

    private async Task ListasAsync(CancellationToken ct)
    {
        ViewData["Categorias"] = new SelectList(await _db.Categorias.OrderBy(c => c.Orden).ToListAsync(ct), "Id", "Nombre");
        ViewData["Marcas"] = new SelectList(await _db.Marcas.OrderBy(m => m.Nombre).ToListAsync(ct), "Id", "Nombre");
        ViewData["Productos"] = new SelectList(await _db.Productos.Where(p => p.Estado == EstadoProducto.Activo).OrderBy(p => p.Nombre).ToListAsync(ct), "Id", "Nombre");
    }

    // ---------- Cupones ----------

    [HttpGet("cupones/nuevo")]
    [Permiso(Modulos.Promociones, P.Crear)]
    public IActionResult NuevoCupon() => View("Cupon", new CuponFormVm());

    [HttpGet("cupones/{id:int}")]
    [Permiso(Modulos.Promociones, P.Editar)]
    public async Task<IActionResult> EditarCupon(int id, CancellationToken ct)
    {
        var c = await _db.Cupones.FindAsync([id], ct);
        if (c is null) return NotFound();
        ViewData["Usos"] = c.UsosActuales;
        return View("Cupon", new CuponFormVm
        {
            Id = c.Id, Codigo = c.Codigo, Descripcion = c.Descripcion, Tipo = c.Tipo, Valor = c.Valor, MontoMinimo = c.MontoMinimo,
            FechaInicio = c.FechaInicio, FechaFin = c.FechaFin, UsosMaximos = c.UsosMaximos, UsosPorUsuario = c.UsosPorUsuario, Activo = c.Activo
        });
    }

    [HttpPost("cupones/nuevo")]
    [Permiso(Modulos.Promociones, P.Crear)]
    public Task<IActionResult> NuevoCupon(CuponFormVm vm, CancellationToken ct) { vm.Id = null; return GuardarCuponAsync(vm, ct); }

    [HttpPost("cupones/{id:int}")]
    [Permiso(Modulos.Promociones, P.Editar)]
    public Task<IActionResult> EditarCupon(int id, CuponFormVm vm, CancellationToken ct) { vm.Id = id; return GuardarCuponAsync(vm, ct); }

    private async Task<IActionResult> GuardarCuponAsync(CuponFormVm vm, CancellationToken ct)
    {
        var codigo = ServicioCupones.Normalizar(vm.Codigo);
        if (await _db.Cupones.AnyAsync(c => c.Codigo == codigo && c.Id != vm.Id, ct)) ModelState.AddModelError(nameof(vm.Codigo), "Ya existe un cupón con ese código.");
        if (!ModelState.IsValid) return View("Cupon", vm);

        var c = vm.Id is null ? _db.Cupones.Add(new Cupon()).Entity : await _db.Cupones.FindAsync([vm.Id.Value], ct);
        if (c is null) return NotFound();
        (c.Codigo, c.Descripcion, c.Tipo, c.Valor, c.MontoMinimo, c.UsosMaximos, c.UsosPorUsuario, c.Activo) =
            (codigo, vm.Descripcion?.Trim(), vm.Tipo, vm.Valor, vm.MontoMinimo, vm.UsosMaximos, vm.UsosPorUsuario, vm.Activo);
        (c.FechaInicio, c.FechaFin) = (DateTime.SpecifyKind(vm.FechaInicio, DateTimeKind.Utc), DateTime.SpecifyKind(vm.FechaFin, DateTimeKind.Utc));
        await _db.SaveChangesAsync(ct);
        return Resultado(null, $"Cupón {codigo} guardado.", nameof(Index));
    }

    /// <summary>Un cupón ya usado en pedidos no se borra (quedaría sin rastro): se desactiva.</summary>
    [HttpPost("cupones/{id:int}/eliminar")]
    [Permiso(Modulos.Promociones, P.Eliminar)]
    public async Task<IActionResult> EliminarCupon(int id, CancellationToken ct)
    {
        if (await _db.Pedidos.AnyAsync(p => p.CuponId == id, ct))
        {
            await _db.Cupones.Where(c => c.Id == id).ExecuteUpdateAsync(s => s.SetProperty(c => c.Activo, false), ct);
            return Resultado(null, "El cupón ya se usó en pedidos: se desactivó en lugar de eliminarse.", nameof(Index));
        }
        await _db.Cupones.Where(c => c.Id == id).ExecuteDeleteAsync(ct);
        return Resultado(null, "Cupón eliminado.", nameof(Index));
    }
}
