using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Windows.Markup;

namespace OpenSweep.Core;

/// <summary>
/// Traducciones sencillas: el texto en español es la clave.
/// Los idiomas están en la carpeta i18n/ del repositorio (compartida con Android):
/// para añadir uno basta con crear i18n/xx.json.
/// </summary>
public static class Loc
{
    private static Dictionary<string, string> _map = [];

    public static string Lang { get; private set; } = "es";

    /// <summary>Idiomas disponibles (además del español).</summary>
    public static IEnumerable<string> Available =>
        Assembly.GetExecutingAssembly().GetManifestResourceNames()
            .Where(n => n.StartsWith("i18n.") && n.EndsWith(".json"))
            .Select(n => n[5..^5]);

    /// <param name="forced">Idioma forzado por línea de comandos (--lang en).</param>
    public static void Init(string? forced = null)
    {
        var pref = forced ?? AppSettings.Current.Language;
        var system = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        Lang = pref != "auto" ? pref : system == "es" ? "es" : Available.Contains(system) ? system : "en";
        _map = [];
        if (Lang == "es") return;
        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"i18n.{Lang}.json");
            if (stream != null) _map = JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? [];
        }
        catch { }
    }

    public static string T(string es) => Lang == "es" ? es : _map.TryGetValue(es, out var tr) ? tr : es;
}

/// <summary>Uso en XAML: Text="{l:T 'Texto en español'}"</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TExtension(string text) : MarkupExtension
{
    public string Text { get; set; } = text;
    public override object ProvideValue(IServiceProvider serviceProvider) => Loc.T(Text);
}
