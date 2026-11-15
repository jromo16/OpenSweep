using Microsoft.Win32;
using OpenSweep.Core;

namespace OpenSweep.Services;

public static partial class UninstallerService
{
    private static readonly HashSet<string> GenericExeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "setup", "install", "installer", "uninstall", "uninst", "unins000", "unins001", "update", "updater", "helper", "launcher",
        "crashpad_handler", "crashreporter", "elevate", "service", "app", "main", "start", "run", "client", "server", "node",
        "python", "pythonw", "java", "javaw", "cmd", "powershell", "rundll32", "msiexec", "explorer",
    };

    private static readonly (RegistryHive hive, string path, string reason)[] TraceKeys =
    [
        (RegistryHive.CurrentUser, @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Compatibility Assistant\Store", "Historial de compatibilidad de Windows"),
        (RegistryHive.CurrentUser, @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers", "Ajuste de compatibilidad"),
        (RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers", "Ajuste de compatibilidad"),
        (RegistryHive.CurrentUser, @"Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\MuiCache", "Caché de nombres de Windows"),
    ];

    /// <summary>
    /// Rastros que deja Windows al usar el programa: su carpeta en %TEMP%, los valores de compatibilidad (AppCompatFlags)
    /// y de nombres (MuiCache) cuyo nombre es una ruta dentro de la carpeta del programa, y los archivos de Prefetch de sus ejecutables.
    /// </summary>
    private static void FindUsageTraces(InstalledProgram p, List<string> tokens, ScanMode mode,
        Action<string, string> addDir, Action<string, string> addFile, Action<RegistryHive, RegistryView, string, string?, string> addKey)
    {
        foreach (var d in Util.SafeDirs(Path.GetTempPath().TrimEnd('\\')))
            if (Matches(d.Name, tokens, mode)) addDir(d.FullName, Loc.T("Archivos temporales del programa"));

        var dirs = KnownDirs(p, tokens);
        var exeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void AddExe(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            path = path.Trim('"');
            if (!path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return;
            var name = Path.GetFileNameWithoutExtension(path);
            if (name.Length >= 3 && !GenericExeNames.Contains(name)) exeNames.Add(name);
        }
        AddExe(PathHelper.ExtractPath(p.DisplayIcon));
        AddExe(PathHelper.ExtractPath(p.UninstallString));
        foreach (var d in dirs.Where(Directory.Exists))
            foreach (var f in Util.SafeFiles(d, "*.exe", true).Take(200)) AddExe(f.FullName);

        if (dirs.Count > 0)
        {
            foreach (var (hive, path, reason) in TraceKeys)
            {
                try
                {
                    using var k = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64).OpenSubKey(path);
                    if (k == null) continue;
                    foreach (var v in k.GetValueNames())
                    {
                        var file = TraceValuePath(v);
                        if (!PointsTo(file, dirs)) continue;
                        addKey(hive, RegistryView.Registry64, path, v, Loc.T(reason));
                        AddExe(file);
                    }
                }
                catch { }
            }
        }

        if (exeNames.Count == 0) return;
        var heads = PrefetchHeads(exeNames);
        foreach (var f in Util.SafeFiles(Path.Combine(Util.WinDir, "Prefetch"), "*.pf", false))
        {
            var dash = f.Name.LastIndexOf('-');
            if (dash > 0 && heads.Contains(f.Name[..dash].ToUpperInvariant()))
                addFile(f.FullName, Loc.T("Rastro de inicio rápido (Prefetch)"));
        }
    }

    /// <summary>Ruta de archivo dentro del nombre de un valor. MuiCache usa «C:\ruta\app.exe.FriendlyAppName».</summary>
    internal static string TraceValuePath(string valueName)
    {
        var i = valueName.IndexOf(".exe.", StringComparison.OrdinalIgnoreCase);
        return i > 0 ? valueName[..(i + 4)] : valueName;
    }

    /// <summary>Cabeceras de los archivos de Prefetch («NOMBRE.EXE», recortado a 29 caracteres como hace Windows).</summary>
    internal static HashSet<string> PrefetchHeads(IEnumerable<string> exeNames)
    {
        var heads = new HashSet<string>();
        foreach (var n in exeNames)
        {
            foreach (var h in new[] { (n + ".EXE").ToUpperInvariant(), n.ToUpperInvariant() })
                heads.Add(h.Length > 29 ? h[..29] : h);
        }
        return heads;
    }
}
