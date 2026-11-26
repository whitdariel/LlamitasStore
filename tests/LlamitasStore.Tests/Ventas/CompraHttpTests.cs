using System.Net;
using System.Text.Json;
using LlamitasStore.Data;
using LlamitasStore.Models.Entities;
using LlamitasStore.Tests.Soporte;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LlamitasStore.Tests.Ventas;

/// <summary>Compra completa por HTTP: visitante agrega, se registra (el carrito se conserva), elige dirección y confirma.</summary>
public sealed class CompraHttpTests : IAsyncLifetime
{
    private readonly FabricaApp _app = new();
    private int _productoId;

    public Task InitializeAsync() => _app.InicializarAsync(async db =>
    {
        var p = new Producto
        {
            Sku = "H-1", Nombre = "Teclado de prueba", Slug = "teclado-de-prueba", DescripcionCorta = "c", Descripcion = "d",
            Categoria = new Categoria { Nombre = "Teclados", Slug = "teclados" }, Marca = new Marca { Nombre = "M", Slug = "m" },
            Precio = 400, PrecioCosto = 200, Stock = 3
        };
        db.Productos.Add(p);
        await db.SaveChangesAsync();
        _productoId = p.Id;
    });

    public Task DisposeAsync() { _app.Dispose(); return Task.CompletedTask; }

    private static string Ubicacion(HttpResponseMessage r) => r.Headers.Location?.OriginalString ?? "";

    [Fact]
    public async Task Compra_completa_de_visitante_a_pedido_confirmado()
    {
        var nav = _app.NuevoNavegador();

        // 1) Visitante agrega 2 unidades (respuesta JSON para el script).
        var agregar = await nav.EnviarAsync("/catalogo", "/carrito/agregar",
            new() { ["productoId"] = _productoId.ToString(), ["cantidad"] = "2" }, new() { ["X-Requested-With"] = "fetch" });
        var json = JsonDocument.Parse(await agregar.Content.ReadAsStringAsync()).RootElement;
        Assert.True(json.GetProperty("ok").GetBoolean());
        Assert.Equal(2, json.GetProperty("total").GetInt32());

        // 2) Ir a checkout sin sesión → login.
        var checkout = await nav.Http.GetAsync("/checkout");
        Assert.Contains("/cuenta/login", Ubicacion(checkout));

        // 3) Registrarse: el carrito del visitante pasa a la cuenta.
        await nav.RegistrarAsync("compradora@prueba.bo");
        Assert.Contains("Teclado de prueba", await (await nav.Http.GetAsync("/carrito")).Content.ReadAsStringAsync());

        // 4) Sin direcciones: el checkout ofrece agregar una y vuelve.
        var nueva = await nav.EnviarAsync("/cuenta/direcciones/nueva?returnUrl=%2Fcheckout", "/cuenta/direcciones/nueva", new()
        {
            ["Alias"] = "Casa", ["Destinatario"] = "Compradora", ["Telefono"] = "71234567", ["Departamento"] = "La Paz",
            ["Ciudad"] = "Viacha", ["DireccionLinea"] = "Calle 9", ["returnUrl"] = "/checkout"
        });
        Assert.Equal("/checkout", Ubicacion(nueva));

        int direccionId;
        using (var s = _app.Services.CreateScope())
            direccionId = await s.ServiceProvider.GetRequiredService<AppDbContext>().Direcciones.Select(d => d.Id).SingleAsync();

        // 5) Confirmar.
        var confirmar = await nav.EnviarAsync("/checkout", "/checkout/confirmar", new()
        {
            ["DireccionId"] = direccionId.ToString(), ["MetodoPago"] = nameof(MetodoPago.Transferencia), ["ReferenciaPago"] = "TRX-123"
        });
        // La URL sale en minúsculas (LowercaseUrls); el número guardado está en mayúsculas.
        Assert.StartsWith("/checkout/confirmado/ls-", Ubicacion(confirmar));
        var numero = Ubicacion(confirmar).Split('/').Last().ToUpperInvariant();

        var pagina = await (await nav.Http.GetAsync(Ubicacion(confirmar))).Content.ReadAsStringAsync();
        Assert.Contains(numero, pagina);

        // 6) Persistencia, stock y correo de confirmación.
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var pedido = await db.Pedidos.Include(p => p.Pagos).SingleAsync(p => p.Numero == numero);
        Assert.Equal(800m + 25m, pedido.Total);
        Assert.Equal("TRX-123", pedido.Pagos.Single().Referencia);
        Assert.Equal(1, (await db.Productos.FindAsync(_productoId))!.Stock);
        Assert.Contains(_app.Correos.Enviados, c => c.Para == "compradora@prueba.bo" && c.Asunto.Contains(numero));

        // 7) Reenviar el formulario (doble clic) no crea otro pedido.
        var repetido = await nav.EnviarAsync("/checkout/confirmado/" + numero, "/checkout/confirmar", new()
        {
            ["DireccionId"] = direccionId.ToString(), ["MetodoPago"] = nameof(MetodoPago.Transferencia)
        });
        Assert.Equal("/cuenta/pedidos", Ubicacion(repetido));
        Assert.Equal(1, await db.Pedidos.CountAsync());

        // 8) Aparece en "Mis pedidos" y otro usuario no puede verlo.
        Assert.Contains(numero, await (await nav.Http.GetAsync("/cuenta/pedidos")).Content.ReadAsStringAsync());
        var otro = _app.NuevoNavegador();
        await otro.RegistrarAsync("curioso@prueba.bo");
        Assert.Equal(HttpStatusCode.NotFound, (await otro.Http.GetAsync($"/cuenta/pedidos/{numero}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await otro.Http.GetAsync($"/checkout/confirmado/{numero}")).StatusCode);
    }

    [Fact]
    public async Task Checkout_con_carrito_vacio_vuelve_al_carrito()
    {
        var nav = _app.NuevoNavegador();
        await nav.RegistrarAsync("vacio@prueba.bo");
        Assert.Equal("/carrito", Ubicacion(await nav.Http.GetAsync("/checkout")));
    }

    [Fact]
    public async Task Cupon_invalido_muestra_el_motivo_en_el_carrito()
    {
        var nav = _app.NuevoNavegador();
        await nav.EnviarAsync("/catalogo", "/carrito/agregar", new() { ["productoId"] = _productoId.ToString(), ["cantidad"] = "1" });
        await nav.EnviarAsync("/carrito", "/carrito/cupon", new() { ["codigo"] = "NO-EXISTE" });

        Assert.Contains("no existe", await (await nav.Http.GetAsync("/carrito")).Content.ReadAsStringAsync());
    }
}
