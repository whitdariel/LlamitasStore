using LlamitasStore.Services.Ventas;
using LlamitasStore.ViewModels;

namespace LlamitasStore.Tests.Ventas;

public sealed class CarritoYCuponesTests : IAsyncLifetime
{
    private EscenarioVentas _e = null!;
    public async Task InitializeAsync() => _e = await EscenarioVentas.CrearAsync();
    public async Task DisposeAsync() => await _e.DisposeAsync();

    private DuenoCarrito Ana => new(_e.Ana, null);

    [Fact]
    public async Task Agregar_suma_cantidades_y_limita_al_stock()
    {
        var (db, carrito, _, _) = _e.Servicios();
        await using (db)
        {
            Assert.Equal(EstadoAgregar.Agregado, (await carrito.AgregarAsync(Ana, _e.A, 3)).Estado);
            var r = await carrito.AgregarAsync(Ana, _e.A, 4); // 3 + 4 > stock 5
            Assert.Equal(EstadoAgregar.Ajustado, r.Estado);
            Assert.Equal(5, r.CantidadEnCarrito);
            Assert.Equal(5, await carrito.ContarAsync(Ana));
        }
    }

    [Fact]
    public async Task No_se_agregan_productos_inactivos_agotados_o_inexistentes()
    {
        var (db, carrito, _, _) = _e.Servicios();
        await using (db)
        {
            Assert.Equal(EstadoAgregar.NoDisponible, (await carrito.AgregarAsync(Ana, _e.C, 1)).Estado);
            Assert.Equal(EstadoAgregar.SinStock, (await carrito.AgregarAsync(Ana, _e.D, 1)).Estado);
            Assert.Equal(EstadoAgregar.NoDisponible, (await carrito.AgregarAsync(Ana, 999999, 1)).Estado);
            Assert.Equal(0, await carrito.ContarAsync(Ana));
        }
    }

    [Fact]
    public async Task Cantidad_cero_quita_y_la_vista_calcula_totales()
    {
        var (db, carrito, _, _) = _e.Servicios();
        await using (db)
        {
            await carrito.AgregarAsync(Ana, _e.A, 1);
            await carrito.AgregarAsync(Ana, _e.B, 1);
            var vista = await carrito.ObtenerAsync(Ana);
            Assert.Equal(1200m, vista.Totales.Subtotal);
            Assert.Equal(25m, vista.Totales.CostoEnvio);
            Assert.True(vista.PuedeComprar);

            await carrito.ActualizarAsync(Ana, _e.B, 0);
            Assert.Single((await carrito.ObtenerAsync(Ana)).Items);
        }
    }

    [Fact]
    public async Task Si_el_stock_baja_despues_de_agregar_el_carrito_lo_advierte_y_bloquea_la_compra()
    {
        var (db, carrito, _, _) = _e.Servicios();
        await using (db)
        {
            await carrito.AgregarAsync(Ana, _e.A, 4);
            var producto = await db.Productos.FindAsync(_e.A);
            producto!.Stock = 2;
            await db.SaveChangesAsync();

            var vista = await carrito.ObtenerAsync(Ana);
            Assert.True(vista.Items[0].CantidadExcedeStock);
            Assert.False(vista.PuedeComprar);
        }
    }

    [Fact]
    public async Task Carrito_de_visitante_se_fusiona_al_iniciar_sesion()
    {
        var visitante = new DuenoCarrito(null, Guid.NewGuid().ToString());
        var (db, carrito, _, _) = _e.Servicios();
        await using (db)
        {
            await carrito.AgregarAsync(Ana, _e.A, 2);
            await carrito.AgregarAsync(visitante, _e.A, 2);
            await carrito.AgregarAsync(visitante, _e.B, 1);

            await carrito.FusionarAsync(visitante.Token!, _e.Ana);

            var vista = await carrito.ObtenerAsync(Ana);
            Assert.Equal(4, vista.Items.Single(i => i.ProductoId == _e.A).Cantidad);
            Assert.Equal(1, vista.Items.Single(i => i.ProductoId == _e.B).Cantidad);
            Assert.Equal(0, await carrito.ContarAsync(visitante)); // el carrito anónimo desaparece
        }
    }

    [Theory]
    [InlineData("p10", 1000, true, null)]                 // no distingue mayúsculas
    [InlineData("P10", 200, false, "compra mínima")]
    [InlineData("VIEJO", 1000, false, "venció")]
    [InlineData("NOEXISTE", 1000, false, "no existe")]
    [InlineData("", 1000, false, "Escribe")]
    public async Task Validacion_de_cupones(string codigo, decimal subtotal, bool valido, string? error)
    {
        var (db, _, _, cupones) = _e.Servicios();
        await using (db)
        {
            var v = await cupones.ValidarAsync(codigo, _e.Ana, subtotal);
            Assert.Equal(valido, v.Valido);
            if (error is not null) Assert.Contains(error, v.Error);
        }
    }

    [Fact]
    public async Task Cupon_aplicado_descuenta_en_la_vista_del_carrito()
    {
        var (db, carrito, _, _) = _e.Servicios();
        await using (db)
        {
            await carrito.AgregarAsync(Ana, _e.A, 1);
            Assert.True((await carrito.AplicarCuponAsync(Ana, "p10")).Valido);

            var vista = await carrito.ObtenerAsync(Ana);
            Assert.Equal("P10", vista.CuponAplicado!.Codigo);
            Assert.Equal(100m, vista.Totales.Descuento);
            Assert.Equal(1000m - 100m + 25m, vista.Totales.Total); // 900 < 1500 → paga envío
        }
    }
}
