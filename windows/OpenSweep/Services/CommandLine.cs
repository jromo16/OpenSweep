using System.Runtime.InteropServices;
using System.Text;
using OpenSweep.Core;

namespace OpenSweep.Services;

/// <summary>
/// Modo línea de comandos (como BleachBit): limpiar sin abrir la ventana, desde scripts o tareas.
///   OpenSweep.exe --clean [--rules lista] [--include-privacy] [--log archivo]
///   OpenSweep.exe --analyze [--rules lista]
///   OpenSweep.exe --list-rules
/// La lista de reglas admite identificadores («br.Google Chrome.cache»), grupos («group:Navegadores») o «all».
/// Sin --rules se usan las reglas marcadas en «Limpieza».
/// </summary>
public static class CommandLine
{
    public static readonly string[] Commands = ["--clean", "--analyze", "--list-rules", "--help", "/?"];

    public static bool IsCommand(string[] args) => args.Any(a => Commands.Contains(a, StringComparer.OrdinalIgnoreCase));

    [DllImport("kernel32.dll")] private static extern bool AttachConsole(int pid);

    private static StringBuilder _log = new();
    private static bool _console;

    private static void Out(string line = "")
    {
        _log.AppendLine(line);
        if (_console) Console.WriteLine(line);
    }

    private static string? Arg(string[] args, string name)
    {
        var i = Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
        return i >= 0 && i + 1 < args.Length && !args[i + 1].StartsWith("--") ? args[i + 1] : null;
    }

    /// <summary>Ejecuta el comando y devuelve el código de salida (0 = bien).</summary>
    public static int Run(string[] args)
    {
        // La app es de ventana: se engancha a la consola desde la que se lanzó (si la hay) para escribir en ella
        _console = AttachConsole(-1);
        if (_console)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.WriteLine();
        }
        _log = new StringBuilder();
        int code;
        try
        {
            code = args.Any(a => a.Equals("--list-rules", StringComparison.OrdinalIgnoreCase)) ? ListRules()
                : args.Any(a => a.Equals("--clean", StringComparison.OrdinalIgnoreCase)) ? Clean(args, dryRun: false)
                : args.Any(a => a.Equals("--analyze", StringComparison.OrdinalIgnoreCase)) ? Clean(args, dryRun: true)
                : Help();
        }
        catch (Exception ex)
        {
            Out($"Error: {ex.Message}");
            code = 1;
        }
        if (Arg(args, "--log") is { } log)
        {
            try { File.WriteAllText(log, _log.ToString(), new UTF8Encoding(true)); } catch { }
        }
        return code;
    }

    private static int Help()
    {
        Out($"OpenSweep {AppUpdater.Current.ToString(3)}");
        Out();
        Out(Loc.T("Uso:"));
        Out("  OpenSweep.exe --clean    [--rules <lista>] [--include-privacy] [--log <archivo>]");
        Out("  OpenSweep.exe --analyze  [--rules <lista>] [--log <archivo>]");
        Out("  OpenSweep.exe --list-rules");
        Out();
        Out(Loc.T("<lista>: identificadores separados por comas, «group:Navegadores» o «all». Sin --rules se usa lo marcado en «Limpieza»."));
        Out(Loc.T("Con «group:» o «all» solo se usan las reglas seguras. Historial, cookies, sesiones y demás reglas delicadas solo se aplican con --include-privacy o si se nombran una a una."));
        Out(Loc.T("OpenSweep pide permisos de administrador; abre la consola como administrador para ver la salida, o usa --log."));
        return 0;
    }

    private static int ListRules()
    {
        foreach (var g in JunkCleaner.BuildRules().GroupBy(r => r.Group))
        {
            Out($"[{g.Key}]");
            foreach (var r in g) Out($"  {(r.Selected ? "*" : " ")} {r.Id,-40} {r.Section} - {r.Name}");
        }
        Out();
        Out(Loc.T("* = marcada en «Limpieza» (se usa si no indicas --rules)"));
        return 0;
    }

    /// <summary>Elige las reglas según --rules (o las marcadas) y --include-privacy.</summary>
    internal static List<CleanRule> SelectRules(List<CleanRule> all, string? spec, bool includePrivacy)
    {
        if (string.IsNullOrWhiteSpace(spec))
            return all.Where(r => r.Selected && (includePrivacy || r.Group != "Privacidad")).ToList();
        var parts = spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var chosen = new List<CleanRule>();
        foreach (var r in all)
        {
            bool named = parts.Any(p => p.Equals(r.Id, StringComparison.OrdinalIgnoreCase));
            bool inGroup = parts.Any(p => p.StartsWith("group:", StringComparison.OrdinalIgnoreCase) && r.Group.Equals(p[6..], StringComparison.OrdinalIgnoreCase));
            bool everything = parts.Any(p => p.Equals("all", StringComparison.OrdinalIgnoreCase));
            // Lo delicado (privacidad, historial, cookies, sesiones… todo lo que viene desmarcado por defecto)
            // solo si se nombra expresamente o se pide --include-privacy
            bool sensitive = r.Group == "Privacidad" || !r.DefaultOn;
            if (named || ((inGroup || everything) && (includePrivacy || !sensitive))) chosen.Add(r);
        }
        return chosen;
    }

    private static int Clean(string[] args, bool dryRun)
    {
        var rules = SelectRules(JunkCleaner.BuildRules(), Arg(args, "--rules"), args.Contains("--include-privacy", StringComparer.OrdinalIgnoreCase));
        if (rules.Count == 0) { Out(Loc.T("No hay reglas que aplicar.")); return 2; }
        Out(dryRun ? Loc.T("Analizando…") : Loc.T("Limpiando…"));
        long total = 0, failed = 0, skipped = 0;
        foreach (var r in rules)
        {
            if (!dryRun && r.Processes.Length > 0 && Util.IsProcessRunning(r.Processes))
            {
                Out($"  {Loc.T("omitido (programa abierto)"),-28} {r.Section} - {r.Name}");
                skipped++;
                continue;
            }
            var res = dryRun ? JunkCleaner.Analyze(r, false, CancellationToken.None) : JunkCleaner.Clean(r, CancellationToken.None);
            total += res.Bytes;
            failed += res.Failed;
            if (res.Bytes > 0 || res.Unknown) Out($"  {res.SizeText,12}  {r.Section} - {r.Name}");
        }
        Out();
        Out(dryRun
            ? $"{Loc.T("Se pueden liberar")} {Util.FormatBytes(total)} ({rules.Count} {Loc.T("reglas")})"
            : $"{Loc.T("Liberados")} {Util.FormatBytes(total)}" + (failed > 0 ? $" · {failed} {Loc.T("archivos en uso")}" : "") + (skipped > 0 ? $" · {skipped} {Loc.T("reglas omitidas")}" : ""));
        if (!dryRun) Scheduler.Log($"Línea de comandos: {Util.FormatBytes(total)} liberados");
        return 0;
    }
}
