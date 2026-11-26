using System.Text.RegularExpressions;

namespace LlamitasStore.Tests.Diseno;

/// <summary>
/// Reglas del sistema de diseño pedidas para el proyecto:
///   1) UNA sola paleta de colores ("Llamitas Noche").
///   2) UNA sola tipografía (Outfit).
/// Revisa CSS propio, todas las vistas Razor y todos los SVG del sitio (Bootstrap en /lib queda fuera).
/// </summary>
public partial class SistemaDisenoTests
{
    private static readonly HashSet<string> Paleta = new(StringComparer.OrdinalIgnoreCase)
    {
        // Noche
        "FFFFFF", "F6F4FB", "ECE8F6", "D9D3EA", "B7AFD0", "8E86AB", "6A6387", "4D4766", "353049", "241F33", "1B1727", "14111D", "0D0B13",
        // Violeta
        "EAE3FF", "D3C5FF", "B39DFB", "957AF2", "7B5CE5", "6545CF", "5034AA",
        // Lana
        "FFF4E4", "FFE6C6", "FFD19A", "F9B26A", "EE9450", "E8724F", "C9553A", "8E5238",
        // Menta
        "5FD3A1", "2FB37E", "1E7F59"
    };

    private static readonly string RaizWeb = BuscarRaizWeb();

    private static string BuscarRaizWeb()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "LlamitasStore.sln"))) dir = dir.Parent;
        return dir is null
            ? throw new InvalidOperationException("No se encontró LlamitasStore.sln")
            : Path.Combine(dir.FullName, "src", "LlamitasStore");
    }

    private static IEnumerable<string> Archivos(string carpeta, string patron) =>
        Directory.EnumerateFiles(Path.Combine(RaizWeb, carpeta), patron, SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}lib{Path.DirectorySeparatorChar}"));

    private static IEnumerable<string> ArchivosConColor() =>
        Archivos("wwwroot", "*.css").Concat(Archivos("wwwroot", "*.svg")).Concat(Archivos("Views", "*.cshtml"));

    private static string Rel(string ruta) => Path.GetRelativePath(RaizWeb, ruta);

    [GeneratedRegex(@"#([0-9A-Fa-f]{6}|[0-9A-Fa-f]{3})\b")]
    private static partial Regex Hex();

    [GeneratedRegex(@"rgba?\(\s*(\d{1,3})[\s,]+(\d{1,3})[\s,]+(\d{1,3})")]
    private static partial Regex Rgb();

    [GeneratedRegex(@"--bs-[a-z-]+-rgb:\s*(\d{1,3}),\s*(\d{1,3}),\s*(\d{1,3})")]
    private static partial Regex BootstrapRgb();

    [GeneratedRegex(@"font-family\s*:\s*([^;}]+)")]
    private static partial Regex FontFamily();

    [Fact]
    public void Todos_los_colores_hex_pertenecen_a_la_paleta()
    {
        var fuera = new List<string>();
        foreach (var archivo in ArchivosConColor())
        {
            foreach (Match m in Hex().Matches(File.ReadAllText(archivo)))
            {
                var hex = m.Groups[1].Value;
                if (hex.Length == 3) hex = string.Concat(hex.Select(c => $"{c}{c}"));
                // Los "#" de anclas o ids (p. ej. href="#contenido", url(#bg)) no son colores de 3/6 hex válidos
                // salvo coincidencia; se filtran por contexto: solo cuentan si van tras ':', '=', '"' o espacio.
                var antes = m.Index > 0 ? File.ReadAllText(archivo)[m.Index - 1] : ' ';
                if (antes is not (':' or ' ' or '"' or '\'' or '(' or ',')) continue;
                if (!Paleta.Contains(hex)) fuera.Add($"{Rel(archivo)}: #{hex}");
            }
        }
        Assert.True(fuera.Count == 0, "Colores fuera de la paleta:\n" + string.Join('\n', fuera.Distinct()));
    }

    [Fact]
    public void Todos_los_colores_rgb_pertenecen_a_la_paleta()
    {
        var fuera = new List<string>();
        foreach (var archivo in Archivos("wwwroot", "*.css"))
        {
            var texto = File.ReadAllText(archivo);
            foreach (var m in Rgb().Matches(texto).Concat(BootstrapRgb().Matches(texto)))
            {
                var hex = string.Concat(Enumerable.Range(1, 3).Select(i => int.Parse(m.Groups[i].Value).ToString("X2")));
                if (!Paleta.Contains(hex)) fuera.Add($"{Rel(archivo)}: {m.Value} (#{hex})");
            }
        }
        Assert.True(fuera.Count == 0, "Colores rgb() fuera de la paleta:\n" + string.Join('\n', fuera.Distinct()));
    }

    [Fact]
    public void Solo_se_declara_la_tipografia_Outfit()
    {
        var css = string.Concat(Archivos("wwwroot", "*.css").Select(File.ReadAllText));
        var familias = Regex.Matches(css, @"@font-face\s*{[^}]*font-family:\s*""([^""]+)""")
            .Select(m => m.Groups[1].Value).Distinct().ToList();
        Assert.Equal(["Outfit"], familias);

        // Cualquier font-family debe empezar por Outfit o por la variable que lo define.
        var otras = FontFamily().Matches(css)
            .Select(m => m.Groups[1].Value.Trim())
            .Where(v => !v.StartsWith("var(--ls-fuente)") && !v.StartsWith("\"Outfit\""))
            .ToList();
        Assert.True(otras.Count == 0, "font-family distintas de Outfit: " + string.Join(" | ", otras));

        var vistasConFuente = Archivos("Views", "*.cshtml").Where(f => File.ReadAllText(f).Contains("font-family")).Select(Rel).ToList();
        Assert.Empty(vistasConFuente);
    }

    [Fact]
    public void Las_vistas_no_usan_estilos_en_linea_ni_scripts_en_linea()
    {
        // La CSP (style-src/script-src 'self') los bloquearía y además saldrían del sistema de diseño.
        var problemas = new List<string>();
        foreach (var archivo in Archivos("Views", "*.cshtml"))
        {
            var texto = File.ReadAllText(archivo);
            if (Regex.IsMatch(texto, @"\sstyle\s*=")) problemas.Add($"{Rel(archivo)}: atributo style");
            if (Regex.IsMatch(texto, @"<style[\s>]")) problemas.Add($"{Rel(archivo)}: <style>");
            if (Regex.IsMatch(texto, @"\son(click|change|submit|load|input)\s*=", RegexOptions.IgnoreCase)) problemas.Add($"{Rel(archivo)}: manejador inline");
            // Excepción: bloques de datos JSON-LD para buscadores (no se ejecutan; la CSP no los bloquea).
            if (Regex.IsMatch(texto, @"<script(?![^>]*\ssrc=)(?![^>]*type=""application/ld\+json"")[^>]*>", RegexOptions.IgnoreCase)) problemas.Add($"{Rel(archivo)}: <script> sin src");
        }
        Assert.True(problemas.Count == 0, string.Join('\n', problemas));
    }

    [Fact]
    public void Las_fuentes_estan_alojadas_en_el_sitio_con_su_licencia()
    {
        var carpeta = Path.Combine(RaizWeb, "wwwroot", "fonts", "outfit");
        Assert.True(File.Exists(Path.Combine(carpeta, "outfit-latin-wght-normal.woff2")));
        Assert.True(File.Exists(Path.Combine(carpeta, "LICENSE.txt")));
        Assert.Contains("Open Font License", File.ReadAllText(Path.Combine(carpeta, "LICENSE.txt")));
    }

    [Fact]
    public void Cada_icono_de_categoria_existe_en_el_sprite()
    {
        var sprite = File.ReadAllText(Path.Combine(RaizWeb, "wwwroot", "images", "iconos.svg"));
        var iconosSemilla = new[] { "cpu", "gpu", "placa", "ram", "ssd", "psu", "gabinete", "cooler", "monitor", "teclado", "raton", "auriculares", "accesorio" };
        foreach (var icono in iconosSemilla)
        {
            Assert.Contains($"id=\"cat-{icono}\"", sprite);
            Assert.True(File.Exists(Path.Combine(RaizWeb, "wwwroot", "images", "productos", $"{icono}.svg")), $"Falta ilustración {icono}.svg");
        }
    }
}
