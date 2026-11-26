using LlamitasStore.Areas.Admin.Models;
using LlamitasStore.Data;
using LlamitasStore.Infraestructura;
using LlamitasStore.Models.Entities;
using LlamitasStore.Utilidades;
using LlamitasStore.ViewComponents;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace LlamitasStore.Areas.Admin.Controllers;

[Route("admin/categorias")]
public class CategoriasController : AdminController
{
    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;

    public CategoriasController(AppDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    [HttpGet("")]
    [Permiso(Modulos.Categorias)]
    public async Task<IActionResult> Index(CancellationToken ct) =>
        View((await _db.Categorias.AsNoTracking().OrderBy(c => c.Orden).Select(c => new { c, n = c.Productos.Count() }).ToListAsync(ct))
            .Select(x => (x.c, x.n)).ToList());

    [HttpGet("nueva")]
    [Permiso(Modulos.Categorias, P.Crear)]
    public IActionResult Nueva() => View("Formulario", new CategoriaFormVm());

    [HttpGet("{id:int}")]
    [Permiso(Modulos.Categorias, P.Editar)]
    public async Task<IActionResult> Editar(int id, CancellationToken ct)
    {
        var c = await _db.Categorias.FindAsync([id], ct);
        if (c is null) return NotFound();
        return View("Formulario", new CategoriaFormVm
        {
            Id = c.Id, Nombre = c.Nombre, Descripcion = c.Descripcion, Icono = c.Icono ?? "accesorio", Orden = c.Orden,
            TipoComponente = c.TipoComponente, Activa = c.Activa
        });
    }

    [HttpPost("nueva")]
    [Permiso(Modulos.Categorias, P.Crear)]
    public Task<IActionResult> Nueva(CategoriaFormVm vm, CancellationToken ct) { vm.Id = null; return GuardarAsync(vm, ct); }

    [HttpPost("{id:int}")]
    [Permiso(Modulos.Categorias, P.Editar)]
    public Task<IActionResult> Editar(int id, CategoriaFormVm vm, CancellationToken ct) { vm.Id = id; return GuardarAsync(vm, ct); }

    private async Task<IActionResult> GuardarAsync(CategoriaFormVm vm, CancellationToken ct)
    {
        var slug = Slugs.Generar(vm.Nombre, 80);
        if (!CategoriaFormVm.Iconos.Contains(vm.Icono)) ModelState.AddModelError(nameof(vm.Icono), "Elige un ícono de la lista.");
        if (!Enum.IsDefined(vm.TipoComponente)) ModelState.AddModelError(nameof(vm.TipoComponente), "Tipo no válido.");
        if (await _db.Categorias.AnyAsync(c => (c.Slug == slug || c.Nombre == vm.Nombre.Trim()) && c.Id != vm.Id, ct))
            ModelState.AddModelError(nameof(vm.Nombre), "Ya existe una categoría con ese nombre.");
        if (!ModelState.IsValid) return View("Formulario", vm);

        var c = vm.Id is null ? _db.Categorias.Add(new Categoria()).Entity : await _db.Categorias.FindAsync([vm.Id.Value], ct);
        if (c is null) return NotFound();
        (c.Nombre, c.Slug, c.Descripcion, c.Icono, c.Orden, c.TipoComponente, c.Activa) =
            (vm.Nombre.Trim(), slug, vm.Descripcion?.Trim(), vm.Icono, vm.Orden, vm.TipoComponente, vm.Activa);
        await _db.SaveChangesAsync(ct);
        _cache.Remove(CategoriasViewComponent.ClaveCache);
        return Resultado(null, "Categoría guardada.", nameof(Index));
    }

    [HttpPost("{id:int}/eliminar")]
    [Permiso(Modulos.Categorias, P.Eliminar)]
    public async Task<IActionResult> Eliminar(int id, CancellationToken ct)
    {
        if (await _db.Productos.AnyAsync(p => p.CategoriaId == id, ct))
            return Resultado("La categoría tiene productos: muévelos o desactiva la categoría.", "", nameof(Index));
        await _db.Categorias.Where(c => c.Id == id).ExecuteDeleteAsync(ct);
        _cache.Remove(CategoriasViewComponent.ClaveCache);
        return Resultado(null, "Categoría eliminada.", nameof(Index));
    }
}

[Route("admin/marcas")]
public class MarcasController : AdminController
{
    private readonly AppDbContext _db;
    public MarcasController(AppDbContext db) => _db = db;

    [HttpGet("")]
    [Permiso(Modulos.Marcas)]
    public async Task<IActionResult> Index(CancellationToken ct) =>
        View((await _db.Marcas.AsNoTracking().OrderBy(m => m.Nombre).Select(m => new { m, n = m.Productos.Count() }).ToListAsync(ct))
            .Select(x => (x.m, x.n)).ToList());

    [HttpGet("nueva")]
    [Permiso(Modulos.Marcas, P.Crear)]
    public IActionResult Nueva() => View("Formulario", new MarcaFormVm());

    [HttpGet("{id:int}")]
    [Permiso(Modulos.Marcas, P.Editar)]
    public async Task<IActionResult> Editar(int id, CancellationToken ct)
    {
        var m = await _db.Marcas.FindAsync([id], ct);
        return m is null ? NotFound() : View("Formulario", new MarcaFormVm
        { Id = m.Id, Nombre = m.Nombre, Descripcion = m.Descripcion, SitioWeb = m.SitioWeb, Destacada = m.Destacada, Activa = m.Activa });
    }

    [HttpPost("nueva")]
    [Permiso(Modulos.Marcas, P.Crear)]
    public Task<IActionResult> Nueva(MarcaFormVm vm, CancellationToken ct) { vm.Id = null; return GuardarAsync(vm, ct); }

    [HttpPost("{id:int}")]
    [Permiso(Modulos.Marcas, P.Editar)]
    public Task<IActionResult> Editar(int id, MarcaFormVm vm, CancellationToken ct) { vm.Id = id; return GuardarAsync(vm, ct); }

    private async Task<IActionResult> GuardarAsync(MarcaFormVm vm, CancellationToken ct)
    {
        var slug = Slugs.Generar(vm.Nombre, 80);
        if (vm.SitioWeb is not null && !vm.SitioWeb.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            ModelState.AddModelError(nameof(vm.SitioWeb), "El sitio web debe empezar con https://");
        if (await _db.Marcas.AnyAsync(m => (m.Slug == slug || m.Nombre == vm.Nombre.Trim()) && m.Id != vm.Id, ct))
            ModelState.AddModelError(nameof(vm.Nombre), "Ya existe una marca con ese nombre.");
        if (!ModelState.IsValid) return View("Formulario", vm);

        var m = vm.Id is null ? _db.Marcas.Add(new Marca()).Entity : await _db.Marcas.FindAsync([vm.Id.Value], ct);
        if (m is null) return NotFound();
        (m.Nombre, m.Slug, m.Descripcion, m.SitioWeb, m.Destacada, m.Activa) = (vm.Nombre.Trim(), slug, vm.Descripcion?.Trim(), vm.SitioWeb?.Trim(), vm.Destacada, vm.Activa);
        await _db.SaveChangesAsync(ct);
        return Resultado(null, "Marca guardada.", nameof(Index));
    }

    [HttpPost("{id:int}/eliminar")]
    [Permiso(Modulos.Marcas, P.Eliminar)]
    public async Task<IActionResult> Eliminar(int id, CancellationToken ct)
    {
        if (await _db.Productos.AnyAsync(p => p.MarcaId == id, ct))
            return Resultado("La marca tiene productos: desactívala en lugar de eliminarla.", "", nameof(Index));
        await _db.Marcas.Where(m => m.Id == id).ExecuteDeleteAsync(ct);
        return Resultado(null, "Marca eliminada.", nameof(Index));
    }
}
