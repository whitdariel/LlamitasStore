using LlamitasStore.Models.Entities;
using LlamitasStore.Services;
using LlamitasStore.Services.Catalogo;
using LlamitasStore.Services.Comunidad;
using LlamitasStore.Tests.Soporte;
using Microsoft.EntityFrameworkCore;

namespace LlamitasStore.Tests.Comunidad;

public sealed class ServiciosComunidadTests : IAsyncLifetime
{
    private static readonly DateTime Hoy = new(2026, 11, 26, 12, 0, 0, DateTimeKind.Utc);
    private BaseDatosPrueba _bd = null!;
    private int _ana, _beto, _cpuA, _cpuB, _teclado, _inactivo;

    public async Task InitializeAsync()
    {
        _bd = await BaseDatosPrueba.CrearAsync();
        await using var db = _bd.NuevoContexto();
        var marca = new Marca { Nombre = "M", Slug = "m" };
        var cpus = new Categoria { Nombre = "Procesadores", Slug = "procesadores", TipoComponente = TipoComponente.Procesador };
        var teclados = new Categoria { Nombre = "Teclados", Slug = "teclados" };
        Producto P(string sku, Categoria c, EstadoProducto e = EstadoProducto.Activo) => new()
        {
            Sku = sku, Nombre = "Prod " + sku, Slug = sku.ToLowerInvariant(), DescripcionCorta = "c", Descripcion = "d",
            Categoria = c, Marca = marca, Precio = 1000, PrecioCosto = 1, Stock = 5, Estado = e
        };
        var cpuA = P("CPU-A", cpus);
        cpuA.Compatibilidad = new ProductoCompatibilidad { Socket = "AM5", TiposMemoria = "DDR5", ConsumoW = 65 };
        cpuA.Especificaciones = [new() { Grupo = "Plataforma", Nombre = "Socket", Valor = "AM5", Orden = 1 }, new() { Grupo = "Procesador", Nombre = "Núcleos", Valor = "6", Orden = 2 }];
        var cpuB = P("CPU-B", cpus);
        cpuB.Compatibilidad = new ProductoCompatibilidad { Socket = "AM5", TiposMemoria = "DDR5", ConsumoW = 120 };
        cpuB.Especificaciones = [new() { Grupo = "Plataforma", Nombre = "Socket", Valor = "AM5", Orden = 1 }, new() { Grupo = "Procesador", Nombre = "Núcleos", Valor = "8", Orden = 2 }];
        var teclado = P("TEC", teclados);
        var inactivo = P("OFF", cpus, EstadoProducto.Inactivo);
        db.Productos.AddRange(cpuA, cpuB, teclado, inactivo);

        var ana = new Usuario { UserName = "ana@x.bo", Email = "ana@x.bo", Nombre = "Ana", Apellido = "A", SecurityStamp = "s1" };
        var beto = new Usuario { UserName = "beto@x.bo", Email = "beto@x.bo", Nombre = "Beto", Apellido = "B", SecurityStamp = "s2" };
        db.Users.AddRange(ana, beto);
        await db.SaveChangesAsync();
        (_ana, _beto, _cpuA, _cpuB, _teclado, _inactivo) = (ana.Id, beto.Id, cpuA.Id, cpuB.Id, teclado.Id, inactivo.Id);
    }

    public async Task DisposeAsync() => await _bd.DisposeAsync();

    private ServicioResenas Resenas() => new(_bd.NuevoContexto(), new RelojFijo(Hoy));

    private async Task<Producto> ProductoAsync(int id)
    {
        await using var db = _bd.NuevoContexto();
        return await db.Productos.AsNoTracking().SingleAsync(p => p.Id == id);
    }

    // ---------- Reseñas ----------

    [Fact]
    public async Task Una_resena_por_usuario_se_edita_y_actualiza_el_promedio()
    {
        Assert.Equal(ResultadoResena.Creada, (await Resenas().GuardarAsync(_ana, _cpuA, new(5, "Excelente", "Rinde muy bien en juegos."))).Resultado);
        Assert.Equal(ResultadoResena.Creada, (await Resenas().GuardarAsync(_beto, _cpuA, new(2, "Se calienta", "Necesita buen disipador."))).Resultado);
        Assert.Equal(3.5m, (await ProductoAsync(_cpuA)).ValoracionPromedio);

        // Ana cambia de opinión: se edita su reseña, no se crea otra.
        Assert.Equal(ResultadoResena.Actualizada, (await Resenas().GuardarAsync(_ana, _cpuA, new(4, "Muy bueno", "Rinde bien, algo caro."))).Resultado);
        var p = await ProductoAsync(_cpuA);
        Assert.Equal(2, p.TotalResenas);
        Assert.Equal(3m, p.ValoracionPromedio);

        var propia = await Resenas().ObtenerPropiaAsync(_ana, _cpuA);
        Assert.Equal(("Muy bueno", 4), (propia!.Titulo, propia.Calificacion));
    }

    [Theory]
    [InlineData(0, "Título", "Comentario largo")]
    [InlineData(6, "Título", "Comentario largo")]
    [InlineData(5, "ab", "Comentario largo")]
    [InlineData(5, "Título", "corto")]
    [InlineData(5, "   ", "Comentario largo")]
    public async Task Datos_invalidos_no_se_guardan(int estrellas, string titulo, string comentario)
    {
        var (r, error) = await Resenas().GuardarAsync(_ana, _cpuA, new(estrellas, titulo, comentario));
        Assert.Equal(ResultadoResena.Invalida, r);
        Assert.NotNull(error);
        await using var db = _bd.NuevoContexto();
        Assert.Equal(0, await db.Resenas.CountAsync());
    }

    [Fact]
    public async Task Producto_inactivo_o_inexistente_no_se_puede_resenar()
    {
        Assert.Equal(ResultadoResena.ProductoNoDisponible, (await Resenas().GuardarAsync(_ana, _inactivo, new(5, "Título", "Comentario largo"))).Resultado);
        Assert.Equal(ResultadoResena.ProductoNoDisponible, (await Resenas().GuardarAsync(_ana, 9999, new(5, "Título", "Comentario largo"))).Resultado);
    }

    [Fact]
    public async Task Compra_verificada_solo_con_pedido_entregado_del_producto()
    {
        await using (var db = _bd.NuevoContexto())
        {
            Pedido Pedido(string numero, EstadoPedido estado, int producto) => new()
            {
                Numero = numero, UsuarioId = _ana, Estado = estado, Subtotal = 1000, Total = 1000, MetodoPago = MetodoPago.ContraEntrega,
                EnvioDestinatario = "Ana", EnvioTelefono = "71234567", EnvioDepartamento = "La Paz", EnvioCiudad = "Viacha", EnvioDireccion = "Calle 1",
                Detalles = [new() { ProductoId = producto, NombreProducto = "x", Sku = "x", PrecioUnitario = 1000, Cantidad = 1, Subtotal = 1000 }]
            };
            db.Pedidos.AddRange(Pedido("LS-1", EstadoPedido.Enviado, _cpuA), Pedido("LS-2", EstadoPedido.Entregado, _cpuB));
            await db.SaveChangesAsync();
        }

        await Resenas().GuardarAsync(_ana, _cpuA, new(5, "Enviado aún", "Todavía no me llegó."));
        await Resenas().GuardarAsync(_ana, _cpuB, new(5, "Recibido", "Funciona perfecto."));

        Assert.False((await Resenas().ObtenerPropiaAsync(_ana, _cpuA))!.CompraVerificada);
        Assert.True((await Resenas().ObtenerPropiaAsync(_ana, _cpuB))!.CompraVerificada);
    }

    [Fact]
    public async Task Editar_una_resena_oculta_no_la_vuelve_a_publicar()
    {
        await Resenas().GuardarAsync(_ana, _cpuA, new(1, "Malo", "No me gustó para nada."));
        await using (var db = _bd.NuevoContexto())
        {
            await db.Resenas.ExecuteUpdateAsync(s => s.SetProperty(r => r.Aprobada, false));
            await ServicioResenas.RecalcularAsync(db, _cpuA);
        }
        Assert.Equal(0, (await ProductoAsync(_cpuA)).TotalResenas);

        await Resenas().GuardarAsync(_ana, _cpuA, new(1, "Malo editado", "Sigue sin gustarme."));

        Assert.False((await Resenas().ObtenerPropiaAsync(_ana, _cpuA))!.Aprobada);
        Assert.Equal(0, (await ProductoAsync(_cpuA)).TotalResenas);
    }

    [Fact]
    public async Task Eliminar_solo_borra_la_propia_y_recalcula()
    {
        await Resenas().GuardarAsync(_ana, _cpuA, new(5, "Excelente", "Rinde muy bien en juegos."));
        await Resenas().GuardarAsync(_beto, _cpuA, new(3, "Normal", "Cumple con lo prometido."));

        Assert.True(await Resenas().EliminarAsync(_ana, _cpuA));
        Assert.False(await Resenas().EliminarAsync(_ana, _cpuA));

        var p = await ProductoAsync(_cpuA);
        Assert.Equal((1, 3m), (p.TotalResenas, p.ValoracionPromedio));
        Assert.NotNull(await Resenas().ObtenerPropiaAsync(_beto, _cpuA));
    }

    // ---------- Notificaciones ----------

    [Fact]
    public async Task Notificaciones_solo_del_dueno()
    {
        int idAna;
        await using (var db = _bd.NuevoContexto())
        {
            var n = new Notificacion { UsuarioId = _ana, Titulo = "Pedido enviado", Mensaje = "Va en camino", Url = "/cuenta/pedidos/LS-1", Fecha = Hoy };
            db.Notificaciones.AddRange(n, new Notificacion { UsuarioId = _ana, Titulo = "Otra", Mensaje = "m", Fecha = Hoy.AddMinutes(1) },
                new Notificacion { UsuarioId = _beto, Titulo = "De Beto", Mensaje = "m", Fecha = Hoy });
            await db.SaveChangesAsync();
            idAna = n.Id;
        }
        var s = new ServicioNotificaciones(_bd.NuevoContexto());

        Assert.Equal(2, await s.ContarNoLeidasAsync(_ana));
        Assert.Equal(["Otra", "Pedido enviado"], (await s.ListarAsync(_ana)).Select(n => n.Titulo));
        Assert.Equal((false, (string?)null), await s.AbrirAsync(_beto, idAna)); // ajena

        Assert.Equal((true, "/cuenta/pedidos/LS-1"), await s.AbrirAsync(_ana, idAna));
        Assert.Equal(1, await s.ContarNoLeidasAsync(_ana));
        Assert.Equal(1, await s.MarcarTodasAsync(_ana));
        Assert.Equal(0, await s.ContarNoLeidasAsync(_ana));
        Assert.Equal(1, await s.ContarNoLeidasAsync(_beto));
    }

    // ---------- Comparador ----------

    [Theory]
    [InlineData("3,1,2", new[] { 3, 1, 2 })]
    [InlineData("1,1,2,x,-5,0, 7 ,8,9", new[] { 1, 2, 7, 8 })] // sin repetidos, válidos y máximo 4
    [InlineData("", new int[0])]
    [InlineData(null, new int[0])]
    public void Lee_ids_del_comparador(string? valor, int[] esperado) =>
        Assert.Equal(esperado, ServicioComparador.LeerIds(valor));

    private ServicioComparador Comparador()
    {
        var db = _bd.NuevoContexto();
        return new ServicioComparador(db, new ServicioCatalogo(db, new ServicioPrecios(db, new RelojFijo(Hoy))));
    }

    [Fact]
    public async Task Comparacion_alinea_datos_marca_diferencias_y_no_repite_filas()
    {
        var c = await Comparador().CompararAsync([_cpuB, _cpuA, _inactivo, 9999]);

        Assert.Equal([_cpuB, _cpuA], c.Productos.Select(p => p.Id)); // respeta el orden, sin inactivos ni inexistentes
        Assert.False(c.CategoriasMezcladas);

        var socket = Assert.Single(c.Filas, f => f.Nombre == "Socket");   // la especificación igual a la de compatibilidad no se repite
        Assert.Equal("Compatibilidad", socket.Grupo);
        Assert.False(socket.Difiere);

        var consumo = Assert.Single(c.Filas, f => f.Nombre == "Consumo máximo");
        Assert.Equal(["120 W", "65 W"], consumo.Valores);
        Assert.True(consumo.Difiere);
        Assert.True(Assert.Single(c.Filas, f => f.Nombre == "Núcleos").Difiere);
    }

    [Fact]
    public async Task Comparar_categorias_distintas_lo_avisa_y_los_datos_faltantes_cuentan_como_diferencia()
    {
        var c = await Comparador().CompararAsync([_cpuA, _teclado]);

        Assert.True(c.CategoriasMezcladas);
        var socket = Assert.Single(c.Filas, f => f.Nombre == "Socket");
        Assert.Equal(["AM5", null], socket.Valores);
        Assert.True(socket.Difiere);
    }

    [Fact]
    public async Task Comparacion_vacia()
    {
        Assert.Same(Comparacion.Vacia, await Comparador().CompararAsync([]));
        Assert.Same(Comparacion.Vacia, await Comparador().CompararAsync([_inactivo]));
    }
}
