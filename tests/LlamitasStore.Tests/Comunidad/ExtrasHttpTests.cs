using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using LlamitasStore.Data;
using LlamitasStore.Models.Entities;
using LlamitasStore.Tests.Soporte;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LlamitasStore.Tests.Comunidad;

/// <summary>Fase 8 por HTTP: contacto, reseñas, notificaciones, comparador, páginas informativas, SEO y accesibilidad.</summary>
public sealed class ExtrasHttpTests : IAsyncLifetime
{
    private readonly FabricaApp _app = new();
    private int _a, _b;

    public Task InitializeAsync() => _app.InicializarAsync(async db =>
    {
        var cat = new Categoria { Nombre = "Procesadores", Slug = "procesadores", TipoComponente = TipoComponente.Procesador };
        var marca = new Marca { Nombre = "AMD", Slug = "amd" };
        Producto P(string sku, int consumo) => new()
        {
            Sku = sku, Nombre = "Ryzen " + sku, Slug = "ryzen-" + sku.ToLowerInvariant(), DescripcionCorta = "CPU de prueba", Descripcion = "d",
            Categoria = cat, Marca = marca, Precio = 1500, PrecioCosto = 1, Stock = 4,
            Compatibilidad = new ProductoCompatibilidad { Socket = "AM5", ConsumoW = consumo },
            Imagenes = [new ProductoImagen { Url = "/images/productos/cpu.svg", TextoAlternativo = "Ryzen " + sku, EsPrincipal = true }]
        };
        var (a, b) = (P("A", 65), P("B", 120));
        db.Productos.AddRange(a, b, P("C", 65), P("D", 65), P("E", 65));
        (await db.Ajustes.SingleAsync(x => x.Clave == ClavesAjuste.EmailContacto)).Valor = "tienda@prueba.bo";
        await db.SaveChangesAsync();
        (_a, _b) = (a.Id, b.Id);
    });

    public Task DisposeAsync() { _app.Dispose(); return Task.CompletedTask; }

    private static string Ubicacion(HttpResponseMessage r) => r.Headers.Location?.OriginalString ?? "";

    private static async Task<string> Html(Navegador n, string url)
    {
        var r = await n.Http.GetAsync(url);
        Assert.True(r.StatusCode == HttpStatusCode.OK, $"{url} devolvió {(int)r.StatusCode}");
        return WebUtility.HtmlDecode(await r.Content.ReadAsStringAsync());
    }

    private async Task<T> ConDbAsync<T>(Func<AppDbContext, Task<T>> f)
    {
        using var scope = _app.Services.CreateScope();
        return await f(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    // ---------- Contacto ----------

    [Fact]
    public async Task Contacto_guarda_el_mensaje_y_avisa_a_la_tienda()
    {
        var nav = _app.NuevoNavegador();
        var r = await nav.EnviarAsync("/contacto", "/contacto", new()
        {
            ["Nombre"] = "Lucía", ["Email"] = "lucia@prueba.bo", ["Asunto"] = "Estado de mi pedido",
            ["NumeroPedido"] = "ls-20261126-00001", ["Mensaje"] = "¿Cuándo llega mi pedido <b>urgente</b>?"
        });
        Assert.Equal("/contacto", Ubicacion(r));
        Assert.Contains("Recibimos tu mensaje", await Html(nav, "/contacto"));

        var m = await ConDbAsync(db => db.MensajesContacto.SingleAsync());
        Assert.Equal("Estado de mi pedido · Pedido LS-20261126-00001", m.Asunto);
        var correo = Assert.Single(_app.Correos.Enviados, c => c.Para == "tienda@prueba.bo");
        Assert.Contains("&lt;b&gt;urgente&lt;/b&gt;", correo.Html); // el contenido del visitante va codificado
    }

    [Fact]
    public async Task Contacto_rechaza_asunto_inventado_y_descarta_bots_en_silencio()
    {
        var nav = _app.NuevoNavegador();
        var invalido = await nav.EnviarAsync("/contacto", "/contacto", new()
        {
            ["Nombre"] = "Lucía", ["Email"] = "lucia@prueba.bo", ["Asunto"] = "Spam", ["Mensaje"] = "Mensaje de prueba largo"
        });
        Assert.Equal(HttpStatusCode.OK, invalido.StatusCode); // vuelve a mostrar el formulario con el error
        Assert.Contains("Elige un asunto de la lista", WebUtility.HtmlDecode(await invalido.Content.ReadAsStringAsync()));

        var bot = await nav.EnviarAsync("/contacto", "/contacto", new()
        {
            ["Nombre"] = "Bot", ["Email"] = "bot@spam.bo", ["Asunto"] = "Otro", ["Mensaje"] = "Compra seguidores baratos", ["SitioWeb"] = "http://spam"
        });
        Assert.Equal("/contacto", Ubicacion(bot));
        Assert.Equal(0, await ConDbAsync(db => db.MensajesContacto.CountAsync()));
    }

    // ---------- Reseñas ----------

    [Fact]
    public async Task Resena_exige_sesion_se_publica_y_se_edita_desde_la_ficha()
    {
        var visitante = _app.NuevoNavegador();
        var ficha = await Html(visitante, "/producto/ryzen-a");
        Assert.Contains("para escribir una reseña", ficha);
        var sinSesion = await visitante.EnviarAsync("/producto/ryzen-a", $"/producto/{_a}/resena",
            new() { ["Calificacion"] = "5", ["Titulo"] = "Hola", ["Comentario"] = "Comentario de visitante" });
        Assert.Contains("/cuenta/login", Ubicacion(sinSesion));

        var ana = _app.NuevoNavegador();
        await ana.RegistrarAsync("ana.resena@prueba.bo", nombre: "Ana");
        var r = await ana.EnviarAsync("/producto/ryzen-a", $"/producto/{_a}/resena",
            new() { ["Calificacion"] = "4", ["Titulo"] = "Muy bueno", ["Comentario"] = "Temperaturas bajas con el disipador incluido." });
        Assert.Equal("/producto/ryzen-a?seccion=resenas#detalle", Ubicacion(r));

        var conResena = await Html(ana, "/producto/ryzen-a?seccion=resenas");
        Assert.Contains("Tu reseña ya está publicada", conResena);
        Assert.Contains("Temperaturas bajas con el disipador incluido.", conResena);
        Assert.Contains("Ana P.", conResena);                      // nombre + inicial del apellido
        Assert.Contains("Editar tu reseña", conResena);
        Assert.Matches("id=\"tab-resenas\"[^>]*aria-selected=\"true\"|aria-selected=\"true\"[^>]*id=\"tab-resenas\"", conResena);

        await ana.EnviarAsync("/producto/ryzen-a", $"/producto/{_a}/resena",
            new() { ["Calificacion"] = "2", ["Titulo"] = "Cambié de opinión", ["Comentario"] = "Se calienta más de lo esperado." });
        var (total, promedio) = await ConDbAsync(async db =>
        {
            var p = await db.Productos.SingleAsync(x => x.Id == _a);
            return (await db.Resenas.CountAsync(), p.ValoracionPromedio);
        });
        Assert.Equal((1, 2m), (total, promedio));

        // Datos inválidos: no se guardan y se explica el motivo.
        await ana.EnviarAsync("/producto/ryzen-a", $"/producto/{_a}/resena", new() { ["Calificacion"] = "9", ["Titulo"] = "x", ["Comentario"] = "y" });
        Assert.Contains("Elige de 1 a 5 estrellas", await Html(ana, "/producto/ryzen-a"));
        Assert.Equal(2m, await ConDbAsync(db => db.Productos.Where(x => x.Id == _a).Select(x => x.ValoracionPromedio).SingleAsync()));

        await ana.EnviarAsync("/producto/ryzen-a", $"/producto/{_a}/resena/eliminar", new());
        Assert.Equal(0, await ConDbAsync(db => db.Resenas.CountAsync()));
    }

    // ---------- Notificaciones ----------

    [Fact]
    public async Task Notificaciones_muestran_contador_se_abren_y_son_privadas()
    {
        var ana = _app.NuevoNavegador();
        await ana.RegistrarAsync("ana.notif@prueba.bo");
        var beto = _app.NuevoNavegador();
        await beto.RegistrarAsync("beto.notif@prueba.bo");

        var id = await ConDbAsync(async db =>
        {
            var usuario = await db.Users.SingleAsync(u => u.Email == "ana.notif@prueba.bo");
            var n = new Notificacion { UsuarioId = usuario.Id, Titulo = "Tu pedido LS-1: Enviado", Mensaje = "Va en camino", Url = "/cuenta/pedidos" };
            db.Notificaciones.AddRange(n, new Notificacion { UsuarioId = usuario.Id, Titulo = "Externa", Mensaje = "m", Url = "https://evil.example" });
            await db.SaveChangesAsync();
            return n.Id;
        });

        var inicio = await Html(ana, "/");
        Assert.Contains("aria-label=\"Notificaciones (2 sin leer)\"", inicio);
        Assert.Contains("Tu pedido LS-1: Enviado", await Html(ana, "/cuenta/notificaciones"));

        // Otro usuario no puede abrir (ni marcar) la notificación de Ana.
        Assert.Equal(HttpStatusCode.NotFound, (await beto.EnviarAsync("/cuenta/notificaciones", $"/cuenta/notificaciones/{id}/abrir", new())).StatusCode);

        var abrir = await ana.EnviarAsync("/cuenta/notificaciones", $"/cuenta/notificaciones/{id}/abrir", new());
        Assert.Equal("/cuenta/pedidos", Ubicacion(abrir));
        Assert.Contains("aria-label=\"Notificaciones (1 sin leer)\"", await Html(ana, "/"));

        // Una URL externa guardada nunca se usa como redirección.
        var externaId = await ConDbAsync(db => db.Notificaciones.Where(n => n.Titulo == "Externa").Select(n => n.Id).SingleAsync());
        var externa = await ana.EnviarAsync("/cuenta/notificaciones", $"/cuenta/notificaciones/{externaId}/abrir", new());
        Assert.Equal("/cuenta/notificaciones", Ubicacion(externa));

        await ana.EnviarAsync("/cuenta/notificaciones", "/cuenta/notificaciones/leer-todas", new());
        Assert.Contains("aria-label=\"Notificaciones (0 sin leer)\"", await Html(ana, "/"));
    }

    // ---------- Comparador ----------

    [Fact]
    public async Task Comparador_agrega_hasta_cuatro_quita_y_comparte_por_enlace()
    {
        var nav = _app.NuevoNavegador();
        var ids = await ConDbAsync(db => db.Productos.OrderBy(p => p.Id).Select(p => p.Id).ToListAsync());

        foreach (var id in ids.Take(4))
        {
            var r = await nav.EnviarAsync("/catalogo", "/comparar/agregar", new() { ["productoId"] = id.ToString(), ["returnUrl"] = "/catalogo" });
            Assert.Equal("/catalogo", Ubicacion(r));
        }
        // El quinto no entra (respuesta JSON para el script).
        var quinto = await nav.EnviarAsync("/catalogo", "/comparar/agregar", new() { ["productoId"] = ids[4].ToString() }, new() { ["X-Requested-With"] = "fetch" });
        var json = JsonDocument.Parse(await quinto.Content.ReadAsStringAsync()).RootElement;
        Assert.False(json.GetProperty("ok").GetBoolean());
        Assert.Equal(4, json.GetProperty("total").GetInt32());

        var pagina = await Html(nav, "/comparar");
        Assert.Contains("4 de 4 productos", pagina);
        Assert.Contains("ls-fila-difiere", pagina); // el consumo de B es distinto
        Assert.Contains($"/comparar?ids={string.Join(",", ids.Take(4))}", pagina);

        await nav.EnviarAsync("/comparar", "/comparar/quitar", new() { ["productoId"] = ids[0].ToString() });
        Assert.Contains("3 de 4 productos", await Html(nav, "/comparar"));

        // El enlace compartido muestra esa comparación aunque el visitante tenga otra lista.
        var otro = _app.NuevoNavegador();
        var compartida = await Html(otro, $"/comparar?ids={_a},{_b}");
        Assert.Contains("Usar esta comparación", compartida);
        Assert.Contains("noindex", compartida);
        Assert.Contains("Todavía no agregaste productos", await Html(otro, "/comparar"));

        Assert.Equal(HttpStatusCode.NotFound, (await nav.EnviarAsync("/comparar", "/comparar/agregar", new() { ["productoId"] = "99999" })).StatusCode);
    }

    // ---------- Páginas informativas y SEO ----------

    [Theory]
    [InlineData("/ayuda", "Preguntas frecuentes")]
    [InlineData("/politicas/envios", "Bs. 25,00")]          // monto real de la configuración
    [InlineData("/politicas/devoluciones", "pendiente")]
    [InlineData("/politicas/terminos", "Compra verificada")]
    [InlineData("/politicas/privacidad", "Comparador")]
    [InlineData("/contacto", "tienda@prueba.bo")]
    public async Task Paginas_informativas_usan_datos_de_la_tienda(string url, string texto) =>
        Assert.Contains(texto, await Html(_app.NuevoNavegador(), url));

    [Fact]
    public async Task Privacidad_antigua_redirige_permanente()
    {
        var r = await _app.NuevoNavegador().Http.GetAsync("/home/privacy");
        Assert.Equal(HttpStatusCode.MovedPermanently, r.StatusCode);
        Assert.Equal("/politicas/privacidad", Ubicacion(r));
    }

    [Fact]
    public async Task Robots_y_sitemap_listan_solo_lo_publico()
    {
        var nav = _app.NuevoNavegador();
        var robots = await Html(nav, "/robots.txt");
        Assert.Contains("Disallow: /admin", robots);
        Assert.Contains("Sitemap: https://localhost/sitemap.xml", robots);

        await ConDbAsync(db => db.Productos.Where(p => p.Id == _b).ExecuteUpdateAsync(s => s.SetProperty(p => p.Estado, EstadoProducto.Inactivo)));
        var sitemap = System.Xml.Linq.XDocument.Parse(await (await nav.Http.GetAsync("/sitemap.xml")).Content.ReadAsStringAsync());
        var urls = sitemap.Descendants().Where(e => e.Name.LocalName == "loc").Select(e => e.Value).ToList();
        Assert.Contains("https://localhost/producto/ryzen-a", urls);
        Assert.DoesNotContain("https://localhost/producto/ryzen-b", urls);
        Assert.Contains("https://localhost/catalogo/procesadores", urls);
        Assert.DoesNotContain(urls, u => u.Contains("/cuenta") || u.Contains("/admin"));
    }

    [Fact]
    public async Task Ficha_incluye_datos_estructurados_validos_y_open_graph()
    {
        var html = await (await _app.NuevoNavegador().Http.GetAsync("/producto/ryzen-a")).Content.ReadAsStringAsync();
        var m = Regex.Match(html, "<script type=\"application/ld\\+json\">(.+?)</script>", RegexOptions.Singleline);
        Assert.True(m.Success);
        var ld = JsonDocument.Parse(m.Groups[1].Value).RootElement;
        Assert.Equal("Product", ld.GetProperty("@type").GetString());
        Assert.Equal("Ryzen A", ld.GetProperty("name").GetString());
        Assert.Equal("BOB", ld.GetProperty("offers").GetProperty("priceCurrency").GetString());
        Assert.Equal("1500.00", ld.GetProperty("offers").GetProperty("price").GetString());
        Assert.False(ld.TryGetProperty("aggregateRating", out _)); // sin reseñas no se inventa valoración

        Assert.Contains("<meta property=\"og:type\" content=\"product\" />", html);
        Assert.Contains("<meta property=\"og:image\" content=\"https://localhost/images/productos/cpu.svg\" />", html);
        Assert.Contains("<link rel=\"canonical\" href=\"https://localhost/producto/ryzen-a\" />", html);
    }

    // ---------- Accesibilidad básica (verificable en el HTML) ----------

    [Theory]
    [InlineData("/")]
    [InlineData("/catalogo")]
    [InlineData("/producto/ryzen-a")]
    [InlineData("/pc-builder")]
    [InlineData("/comparar?ids=1,2")]
    [InlineData("/ayuda")]
    [InlineData("/contacto")]
    [InlineData("/politicas/envios")]
    [InlineData("/cuenta/login")]
    [InlineData("/cuenta/registro")]
    [InlineData("/carrito")]
    public async Task Pagina_publica_cumple_reglas_basicas_de_accesibilidad(string url)
    {
        var html = await (await _app.NuevoNavegador().Http.GetAsync(url)).Content.ReadAsStringAsync();
        var problemas = new List<string>();

        if (!html.Contains("<html lang=\"es\"")) problemas.Add("falta lang=es");
        if (!html.Contains("href=\"#contenido\"")) problemas.Add("falta el enlace para saltar al contenido");
        var h1 = Regex.Matches(html, @"<h1[\s>]").Count;
        if (h1 != 1) problemas.Add($"tiene {h1} <h1>");

        foreach (Match img in Regex.Matches(html, @"<img\b[^>]*>"))
            if (!Regex.IsMatch(img.Value, @"\salt=""")) problemas.Add($"imagen sin alt: {img.Value}");

        // Cada control visible tiene etiqueta: <label for>, aria-label o aria-labelledby.
        var ids = Regex.Matches(html, @"<label\b[^>]*\sfor=""([^""]+)""").Select(x => x.Groups[1].Value).ToHashSet();
        foreach (Match c in Regex.Matches(html, @"<(input|select|textarea)\b[^>]*>"))
        {
            var tag = c.Value;
            if (Regex.IsMatch(tag, @"type=""(hidden|submit|button)""")) continue;
            if (tag.Contains("aria-label") || tag.Contains("aria-labelledby")) continue;
            var id = Regex.Match(tag, @"\sid=""([^""]+)""").Groups[1].Value;
            if (id.Length == 0 || !ids.Contains(id)) problemas.Add($"control sin etiqueta: {tag}");
        }

        // Botones con solo un ícono necesitan nombre accesible.
        foreach (Match b in Regex.Matches(html, @"<button\b([^>]*)>(.*?)</button>", RegexOptions.Singleline))
        {
            var texto = Regex.Replace(Regex.Replace(b.Groups[2].Value, @"<svg.*?</svg>", "", RegexOptions.Singleline), "<[^>]+>", "").Trim();
            if (texto.Length == 0 && !b.Groups[1].Value.Contains("aria-label")) problemas.Add($"botón sin nombre: {b.Value[..Math.Min(120, b.Value.Length)]}");
        }

        Assert.True(problemas.Count == 0, $"{url}:\n" + string.Join('\n', problemas));
    }
}
