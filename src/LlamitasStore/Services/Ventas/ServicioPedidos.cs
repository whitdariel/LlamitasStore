using LlamitasStore.Data;
using LlamitasStore.Models.Entities;
using LlamitasStore.Services.Catalogo;
using LlamitasStore.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace LlamitasStore.Services.Ventas;

/// <summary>Error de negocio dentro de la transacción del pedido: provoca rollback y se muestra al cliente.</summary>
public class ErrorPedido : Exception
{
    public IReadOnlyList<string> Errores { get; }
    public ErrorPedido(params string[] errores) : base(string.Join(" ", errores)) => Errores = errores;
}

public class ServicioPedidos
{
    private readonly AppDbContext _db;
    private readonly ServicioPrecios _precios;
    private readonly ServicioCupones _cupones;
    private readonly AjustesTienda _ajustes;
    private readonly TimeProvider _reloj;
    private readonly ILogger<ServicioPedidos> _logger;

    public ServicioPedidos(AppDbContext db, ServicioPrecios precios, ServicioCupones cupones, AjustesTienda ajustes,
        TimeProvider reloj, ILogger<ServicioPedidos> logger)
    {
        _db = db;
        _precios = precios;
        _cupones = cupones;
        _ajustes = ajustes;
        _reloj = reloj;
        _logger = logger;
    }

    /// <summary>
    /// Convierte el carrito del usuario en un pedido. Todo ocurre en UNA transacción:
    /// si algo falla (stock, cupón, dirección) no se descuenta nada ni se crea el pedido.
    /// </summary>
    public async Task<ResultadoPedido> CrearDesdeCarritoAsync(int usuarioId, CheckoutVm vm, CancellationToken ct = default)
    {
        try
        {
            var numero = await _db.EnTransaccionAsync(c => CrearInternoAsync(usuarioId, vm, c), ct);
            _logger.LogInformation("Pedido {Numero} creado por el usuario {Usuario}", numero, usuarioId);
            return new ResultadoPedido(numero, []);
        }
        catch (ErrorPedido e)
        {
            _db.ChangeTracker.Clear();
            return new ResultadoPedido(null, e.Errores);
        }
    }

    private async Task<string> CrearInternoAsync(int usuarioId, CheckoutVm vm, CancellationToken ct)
    {
        var ahora = _reloj.GetUtcNow().UtcDateTime;

        var direccion = await _db.Direcciones.AsNoTracking().FirstOrDefaultAsync(d => d.Id == vm.DireccionId && d.UsuarioId == usuarioId, ct)
            ?? throw new ErrorPedido("Elige una dirección de envío válida.");
        if (vm.MetodoPago is not (MetodoPago.ContraEntrega or MetodoPago.Transferencia))
            throw new ErrorPedido("Elige un método de pago válido.");

        var carrito = await _db.Carritos
            .Include(c => c.Items).ThenInclude(i => i.Producto)
            .FirstOrDefaultAsync(c => c.UsuarioId == usuarioId, ct);
        if (carrito is null || carrito.Items.Count == 0) throw new ErrorPedido("Tu carrito está vacío.");

        // 1) Validación de disponibilidad con datos frescos.
        var errores = new List<string>();
        foreach (var i in carrito.Items)
        {
            if (i.Producto.Estado != EstadoProducto.Activo) errores.Add($"{i.Producto.Nombre} ya no está disponible.");
            else if (i.Producto.Stock < i.Cantidad)
                errores.Add(i.Producto.Stock == 0
                    ? $"{i.Producto.Nombre} se agotó."
                    : $"Solo quedan {i.Producto.Stock} unidad(es) de {i.Producto.Nombre}.");
        }
        if (errores.Count > 0) throw new ErrorPedido(errores.ToArray());

        // 2) Precios vigentes al momento de comprar (quedan congelados en el pedido).
        var promociones = await _precios.ObtenerPromocionesVigentesAsync(ct);
        var lineas = carrito.Items.Select(i =>
        {
            var p = i.Producto;
            var precio = ServicioPrecios.Calcular(p.Precio, p.PrecioOferta, p.Id, p.MarcaId, p.CategoriaId, promociones).PrecioFinal;
            return (Item: i, Precio: precio);
        }).ToList();

        // 3) Cupón (se revalida aquí: pudo vencer o agotarse desde que se aplicó).
        CuponAplicable? cupon = null;
        if (!string.IsNullOrEmpty(carrito.CodigoCupon))
        {
            var subtotalPrevio = lineas.Sum(l => new LineaCalculo(l.Precio, l.Item.Cantidad).Subtotal);
            var v = await _cupones.ValidarAsync(carrito.CodigoCupon, usuarioId, subtotalPrevio, ct);
            if (!v.Valido) throw new ErrorPedido($"El cupón {carrito.CodigoCupon} ya no se puede usar: {v.Error} Quítalo para continuar.");
            cupon = v.Cupon;
        }

        var totales = CalculadoraTotales.Calcular(lineas.Select(l => new LineaCalculo(l.Precio, l.Item.Cantidad)), cupon,
            _ajustes.ObtenerDecimal(ClavesAjuste.CostoEnvio), _ajustes.ObtenerDecimal(ClavesAjuste.EnvioGratisDesde));

        // 4) Descuento atómico de stock: si otra compra se llevó las unidades, la fila no se actualiza.
        var stockResultante = new Dictionary<int, int>();
        foreach (var (item, _) in lineas)
        {
            var filas = await _db.Productos
                .Where(p => p.Id == item.ProductoId && p.Estado == EstadoProducto.Activo && p.Stock >= item.Cantidad)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.Stock, p => p.Stock - item.Cantidad)
                    .SetProperty(p => p.TotalVendidos, p => p.TotalVendidos + item.Cantidad), ct);
            if (filas == 0) throw new ErrorPedido($"Alguien acaba de comprar las últimas unidades de {item.Producto.Nombre}. Revisa tu carrito.");
            stockResultante[item.ProductoId] = await _db.Productos.Where(p => p.Id == item.ProductoId).Select(p => p.Stock).FirstAsync(ct);
        }

        // 5) Uso del cupón, también atómico (respeta el límite global aunque haya compras simultáneas).
        if (cupon is not null)
        {
            var filas = await _db.Cupones
                .Where(c => c.Id == cupon.Id && (c.UsosMaximos == null || c.UsosActuales < c.UsosMaximos))
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.UsosActuales, c => c.UsosActuales + 1), ct);
            if (filas == 0) throw new ErrorPedido($"El cupón {cupon.Codigo} alcanzó su límite de usos. Quítalo para continuar.");
        }

        // 6) Pedido con copia de precios y de la dirección.
        var pedido = new Pedido
        {
            Numero = "T" + Guid.NewGuid().ToString("N")[..28], // provisional: el definitivo usa el Id
            UsuarioId = usuarioId,
            Fecha = ahora,
            Estado = EstadoPedido.Pendiente,
            Subtotal = totales.Subtotal,
            Descuento = totales.Descuento,
            CostoEnvio = totales.CostoEnvio,
            Total = totales.Total,
            CuponId = cupon?.Id,
            CodigoCupon = cupon?.Codigo,
            MetodoPago = vm.MetodoPago!.Value,
            Notas = string.IsNullOrWhiteSpace(vm.Notas) ? null : vm.Notas.Trim(),
            EnvioDestinatario = direccion.Destinatario,
            EnvioTelefono = direccion.Telefono,
            EnvioDepartamento = direccion.Departamento,
            EnvioCiudad = direccion.Ciudad,
            EnvioDireccion = direccion.DireccionLinea,
            EnvioReferencia = direccion.Referencia
        };
        foreach (var (item, precio) in lineas)
        {
            pedido.Detalles.Add(new DetallePedido
            {
                ProductoId = item.ProductoId, NombreProducto = item.Producto.Nombre, Sku = item.Producto.Sku,
                PrecioUnitario = precio, Cantidad = item.Cantidad,
                Subtotal = new LineaCalculo(precio, item.Cantidad).Subtotal
            });
        }
        pedido.Historial.Add(new PedidoEstadoHistorial { Estado = EstadoPedido.Pendiente, Comentario = "Pedido recibido.", UsuarioId = usuarioId, Fecha = ahora });
        pedido.Pagos.Add(new Pago
        {
            Metodo = vm.MetodoPago.Value, Estado = EstadoPago.Pendiente, Monto = totales.Total, Fecha = ahora,
            Referencia = string.IsNullOrWhiteSpace(vm.ReferenciaPago) ? null : vm.ReferenciaPago.Trim()
        });
        _db.Pedidos.Add(pedido);
        await _db.SaveChangesAsync(ct);

        pedido.Numero = $"LS-{ahora:yyyyMMdd}-{pedido.Id:D5}";

        // 7) Movimientos de inventario (trazabilidad de cada unidad que sale).
        foreach (var (item, _) in lineas)
        {
            _db.MovimientosInventario.Add(new MovimientoInventario
            {
                ProductoId = item.ProductoId, Tipo = TipoMovimiento.Salida, Cantidad = -item.Cantidad,
                StockResultante = stockResultante[item.ProductoId], Motivo = $"Venta {pedido.Numero}",
                PedidoId = pedido.Id, UsuarioId = usuarioId, Fecha = ahora
            });
        }

        _db.Notificaciones.Add(new Notificacion
        {
            UsuarioId = usuarioId, Titulo = $"Pedido {pedido.Numero} recibido",
            Mensaje = "Te avisaremos cuando cambie de estado.", Url = $"/cuenta/pedidos/{pedido.Numero}", Fecha = ahora
        });

        // 8) Vaciar el carrito.
        _db.CarritoItems.RemoveRange(carrito.Items);
        carrito.CodigoCupon = null;

        await _db.SaveChangesAsync(ct);
        return pedido.Numero;
    }

    public Task<List<Pedido>> ListarAsync(int usuarioId, CancellationToken ct = default) =>
        _db.Pedidos.AsNoTracking()
            .Include(p => p.Detalles)
            .Where(p => p.UsuarioId == usuarioId)
            .OrderByDescending(p => p.Fecha)
            .ToListAsync(ct);

    /// <summary>
    /// Los números se guardan en mayúsculas ("LS-…"), pero las URLs generadas van en minúsculas (LowercaseUrls):
    /// se normaliza para no depender de la intercalación de la base de datos.
    /// </summary>
    public static string NormalizarNumero(string? numero) => (numero ?? string.Empty).Trim().ToUpperInvariant();

    /// <summary>null si no existe o si es de otro usuario (no se revela cuál de los dos).</summary>
    public Task<Pedido?> ObtenerAsync(int usuarioId, string numero, CancellationToken ct = default)
    {
        numero = NormalizarNumero(numero);
        return _db.Pedidos.AsNoTracking()
            .Include(p => p.Detalles).ThenInclude(d => d.Producto).ThenInclude(p => p.Imagenes)
            .Include(p => p.Historial)
            .Include(p => p.Pagos)
            .AsSplitQuery()
            .FirstOrDefaultAsync(p => p.Numero == numero && p.UsuarioId == usuarioId, ct);
    }

    /// <summary>
    /// El cliente puede cancelar mientras el pedido está Pendiente. Se devuelve el stock (movimiento Devolución),
    /// se libera el uso del cupón y los pagos pendientes quedan rechazados.
    /// </summary>
    public async Task<bool> CancelarPorClienteAsync(int usuarioId, string numero, CancellationToken ct = default)
    {
        numero = NormalizarNumero(numero);
        try
        {
            return await _db.EnTransaccionAsync(async c =>
            {
                var pedido = await _db.Pedidos.Include(p => p.Detalles).Include(p => p.Pagos)
                    .FirstOrDefaultAsync(p => p.Numero == numero && p.UsuarioId == usuarioId, c);
                if (pedido is null || pedido.Estado != EstadoPedido.Pendiente) return false;

                await DevolverStockAsync(pedido, usuarioId, "Cancelado por el cliente", c);
                pedido.Estado = EstadoPedido.Cancelado;
                pedido.FechaActualizacion = _reloj.GetUtcNow().UtcDateTime;
                pedido.Historial.Add(new PedidoEstadoHistorial { Estado = EstadoPedido.Cancelado, Comentario = "Cancelado por el cliente.", UsuarioId = usuarioId, Fecha = pedido.FechaActualizacion.Value });
                foreach (var pago in pedido.Pagos.Where(p => p.Estado == EstadoPago.Pendiente)) pago.Estado = EstadoPago.Rechazado;
                await _db.SaveChangesAsync(c);
                return true;
            }, ct);
        }
        catch (ErrorPedido)
        {
            _db.ChangeTracker.Clear();
            return false;
        }
    }

    /// <summary>Repone stock, revierte ventas y libera el cupón de un pedido. Reutilizable desde el panel admin.</summary>
    internal async Task DevolverStockAsync(Pedido pedido, int? usuarioId, string motivo, CancellationToken ct)
    {
        var ahora = _reloj.GetUtcNow().UtcDateTime;
        foreach (var d in pedido.Detalles)
        {
            await _db.Productos.Where(p => p.Id == d.ProductoId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.Stock, p => p.Stock + d.Cantidad)
                    .SetProperty(p => p.TotalVendidos, p => p.TotalVendidos >= d.Cantidad ? p.TotalVendidos - d.Cantidad : 0), ct);
            var stock = await _db.Productos.Where(p => p.Id == d.ProductoId).Select(p => p.Stock).FirstAsync(ct);
            _db.MovimientosInventario.Add(new MovimientoInventario
            {
                ProductoId = d.ProductoId, Tipo = TipoMovimiento.Devolucion, Cantidad = d.Cantidad, StockResultante = stock,
                Motivo = $"{motivo} ({pedido.Numero})", PedidoId = pedido.Id, UsuarioId = usuarioId, Fecha = ahora
            });
        }
        if (pedido.CuponId is not null)
        {
            await _db.Cupones.Where(c => c.Id == pedido.CuponId && c.UsosActuales > 0)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.UsosActuales, c => c.UsosActuales - 1), ct);
        }
    }
}
