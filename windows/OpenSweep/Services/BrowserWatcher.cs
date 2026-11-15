using OpenSweep.Core;

namespace OpenSweep.Services;

/// <summary>
/// Limpieza al cerrar el navegador: el monitor de la bandeja comprueba cada pocos segundos qué navegadores
/// están abiertos y, cuando uno se cierra, limpia lo que el usuario tenga marcado para él en «Limpieza».
/// </summary>
public sealed class BrowserWatcher
{
    private HashSet<string> _running = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string[]> _browsers = Browsers();
    private DateTime _browsersLoaded = DateTime.Now;

    /// <summary>Navegadores instalados (nombre de la sección de «Limpieza» → procesos).</summary>
    public static Dictionary<string, string[]> Browsers() =>
        JunkCleaner.BuildRules()
            .Where(r => r.Group == "Navegadores" && r.Processes.Length > 0)
            .GroupBy(r => r.Section)
            .ToDictionary(g => g.Key, g => g.First().Processes, StringComparer.OrdinalIgnoreCase);

    /// <summary>Navegadores que estaban abiertos y ya no lo están.</summary>
    public static List<string> Closed(IEnumerable<string> before, IEnumerable<string> now) =>
        before.Except(now, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Comprueba los procesos y devuelve los navegadores que se acaban de cerrar.</summary>
    public List<string> Poll()
    {
        if ((DateTime.Now - _browsersLoaded).TotalMinutes > 30) { _browsers = Browsers(); _browsersLoaded = DateTime.Now; }
        var now = _browsers.Where(b => Util.IsProcessRunning(b.Value)).Select(b => b.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var closed = Closed(_running, now);
        _running = now;
        return closed;
    }

    /// <summary>Limpia las reglas marcadas de un navegador (si sigue cerrado). Devuelve los bytes liberados.</summary>
    public static long Clean(string browser)
    {
        var rules = JunkCleaner.BuildRules().Where(r => r.Group == "Navegadores" && r.Section.Equals(browser, StringComparison.OrdinalIgnoreCase) && r.Selected).ToList();
        if (rules.Count == 0 || Util.IsProcessRunning(rules[0].Processes)) return 0;
        long freed = 0;
        foreach (var r in rules)
        {
            try { freed += JunkCleaner.Clean(r, CancellationToken.None).Bytes; } catch { }
        }
        Scheduler.Log($"Limpieza al cerrar {browser}: {Util.FormatBytes(freed)} liberados");
        return freed;
    }
}
