using System.Globalization;
using System.Text;

namespace LlamitasStore.Utilidades;

public static class Slugs
{
    /// <summary>"Tarjeta Gráfica RTX 4060 (8GB)" → "tarjeta-grafica-rtx-4060-8gb".</summary>
    public static string Generar(string texto, int maximo = 180)
    {
        if (string.IsNullOrWhiteSpace(texto)) return string.Empty;

        var normalizado = texto.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalizado.Length);
        var guionPendiente = false;

        foreach (var c in normalizado)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;

            if (c is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                if (guionPendiente && sb.Length > 0) sb.Append('-');
                sb.Append(c);
                guionPendiente = false;
            }
            else
            {
                guionPendiente = true;
            }
        }

        var slug = sb.ToString();
        return slug.Length <= maximo ? slug : slug[..maximo].TrimEnd('-');
    }
}
