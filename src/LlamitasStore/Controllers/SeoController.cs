using System.Text;
using System.Xml;
using LlamitasStore.Data;
using LlamitasStore.Models.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LlamitasStore.Controllers;

/// <summary>robots.txt y sitemap.xml generados con el catálogo real (solo productos y categorías activos).</summary>
public class SeoController : Controller
{
    private static readonly string[] PaginasFijas =
        ["/", "/catalogo", "/pc-builder", "/comparar", "/ayuda", "/contacto",
         "/politicas/envios", "/politicas/devoluciones", "/politicas/terminos", "/politicas/privacidad"];

    private readonly AppDbContext _db;
    public SeoController(AppDbContext db) => _db = db;

    private string Base => $"{Request.Scheme}://{Request.Host}";

    [HttpGet("robots.txt")]
    [ResponseCache(Duration = 3600)]
    public IActionResult Robots() => Content(
        "User-agent: *\n" +
        "Disallow: /admin\n" +
        "Disallow: /cuenta\n" +
        "Disallow: /carrito\n" +
        "Disallow: /checkout\n" +
        "Disallow: /catalogo/sugerencias\n" +
        $"Sitemap: {Base}/sitemap.xml\n", "text/plain", Encoding.UTF8);

    [HttpGet("sitemap.xml")]
    [ResponseCache(Duration = 3600)]
    public async Task<IActionResult> Sitemap(CancellationToken ct)
    {
        var categorias = await _db.Categorias.AsNoTracking().Where(c => c.Activa).OrderBy(c => c.Orden).Select(c => c.Slug).ToListAsync(ct);
        var productos = await _db.Productos.AsNoTracking()
            .Where(p => p.Estado == EstadoProducto.Activo && p.Categoria.Activa)
            .OrderBy(p => p.Id)
            .Select(p => new { p.Slug, Fecha = p.FechaActualizacion ?? p.FechaCreacion })
            .ToListAsync(ct);

        var sb = new StringBuilder();
        using (var w = XmlWriter.Create(sb, new XmlWriterSettings { Indent = true }))
        {
            w.WriteStartDocument();
            w.WriteStartElement("urlset", "http://www.sitemaps.org/schemas/sitemap/0.9");
            void Url(string ruta, DateTime? fecha = null)
            {
                w.WriteStartElement("url");
                w.WriteElementString("loc", Base + ruta);
                if (fecha is not null) w.WriteElementString("lastmod", fecha.Value.ToString("yyyy-MM-dd"));
                w.WriteEndElement();
            }
            foreach (var p in PaginasFijas) Url(p);
            foreach (var c in categorias) Url($"/catalogo/{Uri.EscapeDataString(c)}");
            foreach (var p in productos) Url($"/producto/{Uri.EscapeDataString(p.Slug)}", p.Fecha);
            w.WriteEndElement();
            w.WriteEndDocument();
        }
        // StringBuilder es UTF-16: se reemplaza la declaración para que coincida con la respuesta UTF-8.
        return Content(sb.ToString().Replace("encoding=\"utf-16\"", "encoding=\"utf-8\""), "application/xml", Encoding.UTF8);
    }
}
