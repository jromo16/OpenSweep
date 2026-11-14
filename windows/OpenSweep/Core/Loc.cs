using System.Globalization;
using System.Windows.Markup;

namespace OpenSweep.Core;

/// <summary>
/// Traducciones sencillas: el texto en español es la clave.
/// Para añadir un idioma, crea un diccionario como <see cref="LangEn"/> y regístralo en <see cref="Languages"/>.
/// </summary>
public static class Loc
{
    private static readonly Dictionary<string, IReadOnlyDictionary<string, string>> Languages = new()
    {
        ["en"] = LangEn.Map,
    };

    public static string Lang { get; private set; } = "es";

    public static void Init()
    {
        var pref = AppSettings.Current.Language;
        Lang = pref == "auto"
            ? (CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "es" ? "es" : "en")
            : pref;
    }

    public static string T(string es) =>
        Lang == "es" ? es : Languages.TryGetValue(Lang, out var d) && d.TryGetValue(es, out var tr) ? tr : es;
}

/// <summary>Uso en XAML: Text="{l:T 'Texto en español'}"</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TExtension(string text) : MarkupExtension
{
    public string Text { get; set; } = text;
    public override object ProvideValue(IServiceProvider serviceProvider) => Loc.T(Text);
}
