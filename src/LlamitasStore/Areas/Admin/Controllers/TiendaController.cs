using LlamitasStore.Areas.Admin.Models;
using LlamitasStore.Data;
using LlamitasStore.Infraestructura;
using LlamitasStore.Models.Entities;
using LlamitasStore.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace LlamitasStore.Areas.Admin.Controllers;

[Route("admin/resenas")]
public class ResenasController : AdminController
{
    private readonly AppDbContext _db;
    public ResenasController(AppDbContext db) => _db = db;

    [HttpGet("")]
    [Permiso(Modulos.Resenas)]
    public async Task<IActionResult> Index(CancellationToken ct) =>
        View(await _db.Resenas.AsNoTracking().Include(r => r.Producto).Include(r => r.Usuario).OrderByDescending(r => r.Fecha).Take(100).ToListAsync(ct));

    /// <summary>Ocultar o volver a mostrar. Recalcula el promedio del producto con las reseñas visibles.</summary>
    [HttpPost("{id:int}/visibilidad")]
    [Permiso(Modulos.Resenas, P.Editar)]
    public async Task<IActionResult> CambiarVisibilidad(int id, bool aprobada, CancellationToken ct)
    {
        var r = await _db.Resenas.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) return NotFound();
        r.Aprobada = aprobada;
        await _db.SaveChangesAsync(ct);
        await RecalcularAsync(r.ProductoId, ct);
        return Resultado(null, aprobada ? "Reseña visible." : "Reseña oculta.", nameof(Index));
    }

    private async Task RecalcularAsync(int productoId, CancellationToken ct)
    {
        var visibles = await _db.Resenas.Where(x => x.ProductoId == productoId && x.Aprobada).Select(x => x.Calificacion).ToListAsync(ct);
        var promedio = visibles.Count == 0 ? 0m : Math.Round((decimal)visibles.Average(), 2);
        await _db.Productos.Where(p => p.Id == productoId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.ValoracionPromedio, promedio).SetProperty(p => p.TotalResenas, visibles.Count), ct);
    }
}

[Route("admin/mensajes")]
public class MensajesController : AdminController
{
    private readonly AppDbContext _db;
    public MensajesController(AppDbContext db) => _db = db;

    [HttpGet("")]
    [Permiso(Modulos.Mensajes)]
    public async Task<IActionResult> Index(CancellationToken ct) =>
        View(await _db.MensajesContacto.AsNoTracking().OrderBy(m => m.Atendido).ThenByDescending(m => m.Fecha).Take(100).ToListAsync(ct));

    [HttpPost("{id:int}/atendido")]
    [Permiso(Modulos.Mensajes, P.Editar)]
    public async Task<IActionResult> MarcarAtendido(int id, bool atendido, CancellationToken ct)
    {
        await _db.MensajesContacto.Where(m => m.Id == id).ExecuteUpdateAsync(s => s.SetProperty(m => m.Atendido, atendido), ct);
        return Resultado(null, atendido ? "Mensaje marcado como atendido." : "Mensaje marcado como pendiente.", nameof(Index));
    }
}

[Route("admin/configuracion")]
public class ConfiguracionController : AdminController
{
    private readonly AppDbContext _db;
    private readonly AjustesTienda _ajustes;

    public ConfiguracionController(AppDbContext db, AjustesTienda ajustes)
    {
        _db = db;
        _ajustes = ajustes;
    }

    [HttpGet("")]
    [Permiso(Modulos.Configuracion)]
    public IActionResult Index() => View(new ConfiguracionVm
    {
        NombreTienda = _ajustes.NombreTienda,
        Moneda = _ajustes.Moneda,
        Email = _ajustes.Obtener(ClavesAjuste.EmailContacto),
        Telefono = _ajustes.Obtener(ClavesAjuste.TelefonoContacto),
        Direccion = _ajustes.Obtener(ClavesAjuste.DireccionTienda),
        CostoEnvio = _ajustes.ObtenerDecimal(ClavesAjuste.CostoEnvio),
        EnvioGratisDesde = _ajustes.ObtenerDecimal(ClavesAjuste.EnvioGratisDesde),
        InstruccionesTransferencia = _ajustes.Obtener(ClavesAjuste.DatosTransferencia)
    });

    [HttpPost("")]
    [Permiso(Modulos.Configuracion, P.Editar)]
    public async Task<IActionResult> Index(ConfiguracionVm vm, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(vm);
        var inv = CultureInfo.InvariantCulture;
        var valores = new Dictionary<string, string>
        {
            [ClavesAjuste.NombreTienda] = vm.NombreTienda.Trim(),
            [ClavesAjuste.SimboloMoneda] = vm.Moneda.Trim(),
            [ClavesAjuste.EmailContacto] = vm.Email?.Trim() ?? "",
            [ClavesAjuste.TelefonoContacto] = vm.Telefono?.Trim() ?? "",
            [ClavesAjuste.DireccionTienda] = vm.Direccion?.Trim() ?? "",
            [ClavesAjuste.CostoEnvio] = vm.CostoEnvio.ToString("0.00", inv),
            [ClavesAjuste.EnvioGratisDesde] = vm.EnvioGratisDesde.ToString("0.00", inv),
            [ClavesAjuste.DatosTransferencia] = vm.InstruccionesTransferencia.Trim()
        };
        var existentes = await _db.Ajustes.ToDictionaryAsync(a => a.Clave, ct);
        foreach (var (clave, valor) in valores)
        {
            if (existentes.TryGetValue(clave, out var a)) a.Valor = valor;
            else _db.Ajustes.Add(new AjusteTienda { Clave = clave, Valor = valor });
        }
        await _db.SaveChangesAsync(ct);
        _ajustes.Invalidar();
        return Resultado(null, "Configuración guardada. Los cambios ya se ven en la tienda.", nameof(Index));
    }
}
