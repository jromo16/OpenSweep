namespace OpenSweep.Core;

public static class PathHelper
{
    /// <summary>
    /// Extrae la ruta del ejecutable de una línea de comandos como
    /// "\"C:\Program Files\X\x.exe\" /arg" o "C:\X\x.exe,0" o "rundll32.exe C:\x.dll,Func".
    /// </summary>
    public static string? ExtractPath(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine)) return null;
        var s = Environment.ExpandEnvironmentVariables(commandLine.Trim());
        if (s.StartsWith('@')) s = s[1..];

        string path;
        if (s.StartsWith('"'))
        {
            var end = s.IndexOf('"', 1);
            path = end > 0 ? s[1..end] : s.Trim('"');
        }
        else
        {
            // Busca la extensión ejecutable más temprana para cortar argumentos sin comillas.
            path = s;
            foreach (var ext in new[] { ".exe", ".dll", ".com", ".bat", ".cmd", ".ocx", ".cpl", ".msc", ".ico", ".lnk", ".sys", ".chm", ".hlp", ".scr" })
            {
                var i = s.IndexOf(ext, StringComparison.OrdinalIgnoreCase);
                if (i > 0)
                {
                    var candidate = s[..(i + ext.Length)];
                    if (path == s || candidate.Length < path.Length) path = candidate;
                }
            }
            var comma = path.LastIndexOf(',');
            if (comma > 2) path = path[..comma];
        }
        path = path.Trim();
        if (path.Length == 0) return null;

        // rundll32 / msiexec: la ruta interesante es el argumento
        var name = Path.GetFileName(path).ToLowerInvariant();
        if (name is "rundll32.exe" or "rundll32")
        {
            var rest = s[(s.IndexOf(name, StringComparison.OrdinalIgnoreCase) + name.Length)..].Trim().Trim('"');
            var comma = rest.IndexOf(',');
            if (comma > 0) rest = rest[..comma];
            return rest.Trim().Trim('"');
        }
        return path;
    }

    /// <summary>Comprueba existencia de un archivo teniendo en cuenta System32/SysWOW64 y rutas relativas al PATH.</summary>
    public static bool FileExistsSmart(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return true; // sin dato -> no lo marcamos como problema
        try
        {
            path = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
            if (path.Contains('%')) return true; // variable no resoluble: no arriesgar
            if (!Path.IsPathRooted(path))
            {
                // Archivo sin ruta: buscar en System32, Windows y PATH
                foreach (var dir in new[] { Environment.SystemDirectory, Util.WinDir }.Concat(Util.Env("PATH").Split(';')))
                {
                    if (string.IsNullOrWhiteSpace(dir)) continue;
                    try
                    {
                        var full = Path.Combine(dir, path);
                        if (File.Exists(full) || File.Exists(full + ".exe")) return true;
                    }
                    catch { }
                }
                return true; // no sabemos dónde está: ser conservadores
            }
            if (File.Exists(path) || Directory.Exists(path) || File.Exists(path + ".exe")) return true;
            // Apps de la Store: la carpeta WindowsApps no es accesible ni para administradores
            if (path.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase)) return true;

            var sys32 = Environment.SystemDirectory;
            var wow = Path.Combine(Util.WinDir, "SysWOW64");
            var sysnative = Path.Combine(Util.WinDir, "Sysnative");
            if (path.StartsWith(sys32, StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(wow + path[sys32.Length..])) return true;
            }
            if (path.StartsWith(sysnative, StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(sys32 + path[sysnative.Length..])) return true;
            }
            // Program Files (x86) <-> Program Files
            var pf = Util.Env("ProgramFiles");
            var pf86 = Util.Env("ProgramFiles(x86)");
            if (pf86.Length > 0 && path.StartsWith(pf86, StringComparison.OrdinalIgnoreCase) && File.Exists(pf + path[pf86.Length..])) return true;
            // Unidad no presente (USB, red): no lo consideramos inválido
            var root = Path.GetPathRoot(path);
            if (!string.IsNullOrEmpty(root) && !Directory.Exists(root)) return true;
            if (path.StartsWith(@"\\")) return true;
            return false;
        }
        catch { return true; }
    }

    private static readonly string[] ProtectedRoots =
    [
        Util.WinDir,
        Environment.SystemDirectory,
        Util.Env("ProgramFiles"),
        Util.Env("ProgramFiles(x86)"),
        Util.Env("ProgramW6432"),
        Util.ProgramData,
        Util.UserProfile,
        Util.LocalAppData,
        Util.RoamingAppData,
        Path.Combine(Util.UserProfile, "AppData"),
        Path.Combine(Util.UserProfile, "AppData", "LocalLow"),
        Path.Combine(Util.UserProfile, "Documents"),
        Path.Combine(Util.UserProfile, "Desktop"),
        Path.Combine(Util.UserProfile, "Downloads"),
        Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
        Environment.GetFolderPath(Environment.SpecialFolder.Programs),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
        Path.Combine(Util.Env("ProgramFiles"), "Common Files"),
        Path.Combine(Util.Env("ProgramFiles(x86)"), "Common Files"),
        Path.Combine(Util.ProgramData, "Microsoft"),
        Path.Combine(Util.LocalAppData, "Microsoft"),
        Path.Combine(Util.RoamingAppData, "Microsoft"),
        Path.Combine(Util.LocalAppData, "Programs"),
        Path.Combine(Util.LocalAppData, "Packages"),
        Path.Combine(Util.LocalAppData, "Temp"),
        @"C:\Users",
    ];

    /// <summary>Indica si una carpeta es crítica y nunca debe eliminarse completa.</summary>
    public static bool IsProtectedFolder(string path)
    {
        try
        {
            var full = Path.GetFullPath(path).TrimEnd('\\');
            if (full.Length <= 3) return true; // raíz de unidad
            foreach (var p in ProtectedRoots)
            {
                if (string.IsNullOrEmpty(p)) continue;
                if (string.Equals(full, p.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return true;
            }
            if (full.StartsWith(Util.WinDir, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
        catch { return true; }
    }
}
