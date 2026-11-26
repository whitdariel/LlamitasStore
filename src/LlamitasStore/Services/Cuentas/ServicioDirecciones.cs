using LlamitasStore.Data;
using LlamitasStore.Models.Entities;
using LlamitasStore.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace LlamitasStore.Services.Cuentas;

public static class Bolivia
{
    public static readonly string[] Departamentos =
        ["Beni", "Chuquisaca", "Cochabamba", "La Paz", "Oruro", "Pando", "Potosí", "Santa Cruz", "Tarija"];
}

public enum ResultadoDireccion { Ok, NoEncontrada, LimiteAlcanzado, DepartamentoInvalido }

/// <summary>
/// Libreta de direcciones. TODAS las consultas filtran por el usuario dueño: un id ajeno se trata como inexistente
/// (evita que alguien edite o borre direcciones de otra persona cambiando el número en la URL).
/// Siempre hay exactamente una predeterminada si el usuario tiene al menos una dirección.
/// </summary>
public class ServicioDirecciones
{
    public const int Maximo = 10;
    private readonly AppDbContext _db;

    public ServicioDirecciones(AppDbContext db) => _db = db;

    public Task<List<Direccion>> ListarAsync(int usuarioId, CancellationToken ct = default) =>
        _db.Direcciones.AsNoTracking()
            .Where(d => d.UsuarioId == usuarioId)
            .OrderByDescending(d => d.EsPredeterminada).ThenBy(d => d.Alias)
            .ToListAsync(ct);

    public Task<Direccion?> ObtenerAsync(int usuarioId, int id, CancellationToken ct = default) =>
        _db.Direcciones.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id && d.UsuarioId == usuarioId, ct);

    public Task<ResultadoDireccion> CrearAsync(int usuarioId, DireccionVm vm, CancellationToken ct = default)
    {
        if (!Bolivia.Departamentos.Contains(vm.Departamento)) return Task.FromResult(ResultadoDireccion.DepartamentoInvalido);

        return _db.EnTransaccionAsync(async c =>
        {
            var cantidad = await _db.Direcciones.CountAsync(d => d.UsuarioId == usuarioId, c);
            if (cantidad >= Maximo) return ResultadoDireccion.LimiteAlcanzado;

            var direccion = new Direccion { UsuarioId = usuarioId };
            Copiar(vm, direccion);
            direccion.EsPredeterminada = cantidad == 0 || vm.EsPredeterminada; // la primera siempre es la predeterminada

            if (direccion.EsPredeterminada) await QuitarPredeterminadaAsync(usuarioId, c);
            _db.Direcciones.Add(direccion);
            await _db.SaveChangesAsync(c);
            return ResultadoDireccion.Ok;
        }, ct);
    }

    public Task<ResultadoDireccion> EditarAsync(int usuarioId, int id, DireccionVm vm, CancellationToken ct = default)
    {
        if (!Bolivia.Departamentos.Contains(vm.Departamento)) return Task.FromResult(ResultadoDireccion.DepartamentoInvalido);

        return _db.EnTransaccionAsync(async c =>
        {
            var direccion = await _db.Direcciones.FirstOrDefaultAsync(d => d.Id == id && d.UsuarioId == usuarioId, c);
            if (direccion is null) return ResultadoDireccion.NoEncontrada;

            var eraPredeterminada = direccion.EsPredeterminada;
            Copiar(vm, direccion);
            // La predeterminada no se "desmarca": se cambia eligiendo otra.
            direccion.EsPredeterminada = eraPredeterminada || vm.EsPredeterminada;

            if (direccion.EsPredeterminada && !eraPredeterminada) await QuitarPredeterminadaAsync(usuarioId, c, excepto: id);
            await _db.SaveChangesAsync(c);
            return ResultadoDireccion.Ok;
        }, ct);
    }

    public Task<ResultadoDireccion> MarcarPredeterminadaAsync(int usuarioId, int id, CancellationToken ct = default) =>
        _db.EnTransaccionAsync(async c =>
        {
            var direccion = await _db.Direcciones.FirstOrDefaultAsync(d => d.Id == id && d.UsuarioId == usuarioId, c);
            if (direccion is null) return ResultadoDireccion.NoEncontrada;

            await QuitarPredeterminadaAsync(usuarioId, c, excepto: id);
            direccion.EsPredeterminada = true;
            await _db.SaveChangesAsync(c);
            return ResultadoDireccion.Ok;
        }, ct);

    public Task<ResultadoDireccion> EliminarAsync(int usuarioId, int id, CancellationToken ct = default) =>
        _db.EnTransaccionAsync(async c =>
        {
            var direccion = await _db.Direcciones.FirstOrDefaultAsync(d => d.Id == id && d.UsuarioId == usuarioId, c);
            if (direccion is null) return ResultadoDireccion.NoEncontrada;

            _db.Direcciones.Remove(direccion);
            await _db.SaveChangesAsync(c);

            if (direccion.EsPredeterminada)
            {
                var siguiente = await _db.Direcciones.Where(d => d.UsuarioId == usuarioId).OrderBy(d => d.Id).FirstOrDefaultAsync(c);
                if (siguiente is not null)
                {
                    siguiente.EsPredeterminada = true;
                    await _db.SaveChangesAsync(c);
                }
            }
            return ResultadoDireccion.Ok;
        }, ct);

    private Task QuitarPredeterminadaAsync(int usuarioId, CancellationToken ct, int excepto = 0) =>
        _db.Direcciones
            .Where(d => d.UsuarioId == usuarioId && d.EsPredeterminada && d.Id != excepto)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.EsPredeterminada, false), ct);

    private static void Copiar(DireccionVm vm, Direccion d)
    {
        d.Alias = vm.Alias.Trim();
        d.Destinatario = vm.Destinatario.Trim();
        d.Telefono = vm.Telefono.Trim();
        d.Departamento = vm.Departamento;
        d.Ciudad = vm.Ciudad.Trim();
        d.DireccionLinea = vm.DireccionLinea.Trim();
        d.Referencia = string.IsNullOrWhiteSpace(vm.Referencia) ? null : vm.Referencia.Trim();
    }
}
