using System.Net.Http;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using OpenSweep.Core;

namespace OpenSweep.Services;

/// <summary>
/// Reglas de limpieza de la comunidad Winapp2 (https://github.com/MoscaDotTo/Winapp2), el mismo formato que usan
/// CCleaner y BleachBit. El archivo NO se incluye en OpenSweep: se descarga a petición del usuario y conserva su
/// propia licencia (CC BY-SA 4.0, atribución al proyecto Winapp2).
///
/// Por seguridad: solo se usan reglas de archivos (no de registro), solo de aplicaciones, juegos, multimedia,
/// utilidades e internet (los navegadores y Windows ya los cubren las reglas propias de OpenSweep), solo de programas
/// instalados, todas desmarcadas por defecto, y nunca se vacía por completo una carpeta del sistema.
/// </summary>
public static partial class Winapp2
{
    public const string Url = "https://raw.githubusercontent.com/MoscaDotTo/Winapp2/master/Winapp2.ini";
    public const string ProjectUrl = "https://github.com/MoscaDotTo/Winapp2";
    public static string FilePath => Path.Combine(AppSettings.Folder, "winapp2.ini");
    public const string Group = "Comunidad (winapp2)";

    // LangSecRef incluidos: 3021 Aplicaciones, 3022 Internet, 3023 Multimedia, 3024 Utilidades
    private static readonly Dictionary<string, string> Categories = new()
    {
        ["3021"] = "Aplicaciones", ["3022"] = "Internet", ["3023"] = "Multimedia", ["3024"] = "Utilidades",
    };

    public sealed class Entry
    {
        public required string Name { get; init; }
        public required string Category { get; init; }
        public List<string> Detect { get; } = [];
        public List<string> DetectFile { get; } = [];
        public List<string> FileKeys { get; } = [];
        public List<string> ExcludeKeys { get; } = [];
        public string? Warning { get; set; }
    }

    // ---------- Descarga ----------
    public static async Task<(bool ok, string message)> DownloadAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("OpenSweep");
            var text = await http.GetStringAsync(Url);
            if (!text.Contains("FileKey1=") || !text.Contains("LangSecRef=")) return (false, Loc.T("El archivo descargado no tiene el formato esperado."));
            Directory.CreateDirectory(AppSettings.Folder);
            var tmp = FilePath + ".tmp";
            await File.WriteAllTextAsync(tmp, text);
            File.Move(tmp, FilePath, overwrite: true);
            _cache = null;
            return (true, $"{Parse().Count} {Loc.T("reglas disponibles")}");
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    public static bool Installed => File.Exists(FilePath);

    /// <summary>Versión indicada en la cabecera del archivo (p. ej. «260915»).</summary>
    public static string? Version()
    {
        try
        {
            foreach (var line in File.ReadLines(FilePath).Take(5))
                if (line.StartsWith("; Version:")) return line[10..].Trim();
        }
        catch { }
        return null;
    }

    public static void Remove()
    {
        try { File.Delete(FilePath); } catch { }
        _cache = null;
    }

    // ---------- Lectura ----------
    private static (DateTime stamp, List<Entry> entries)? _cache;

    public static List<Entry> Parse()
    {
        if (!File.Exists(FilePath)) return [];
        var stamp = File.GetLastWriteTimeUtc(FilePath);
        if (_cache is { } c && c.stamp == stamp) return c.entries;
        var entries = Parse(FilePath);
        _cache = (stamp, entries);
        return entries;
    }

    /// <summary>Lee un archivo con el formato de winapp2.ini (sin caché).</summary>
    public static List<Entry> Parse(string file)
    {
        var list = new List<Entry>();
        string? name = null;
        var fields = new List<(string key, string value)>();
        void Flush()
        {
            if (name == null) return;
            string? cat = null;
            foreach (var (k, v) in fields)
            {
                if (k == "LangSecRef" && Categories.TryGetValue(v, out var c2)) cat = c2;
                if (k == "Section" && !v.Contains("Browser", StringComparison.OrdinalIgnoreCase)) cat = v == "Games" ? "Juegos" : v;
            }
            if (cat != null)
            {
                var e = new Entry { Name = name.EndsWith(" *") ? name[..^2] : name, Category = cat };
                foreach (var (k, v) in fields)
                {
                    if (k.StartsWith("DetectFile")) e.DetectFile.Add(v);
                    else if (k.StartsWith("Detect") && !k.StartsWith("DetectOS")) e.Detect.Add(v);
                    else if (k.StartsWith("FileKey")) e.FileKeys.Add(v);
                    else if (k.StartsWith("ExcludeKey")) e.ExcludeKeys.Add(v);
                    else if (k == "Warning") e.Warning = v;
                }
                if (e.FileKeys.Count > 0) list.Add(e);
            }
            fields.Clear();
        }

        foreach (var raw in File.ReadLines(file))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(';')) continue;
            if (line.StartsWith('[') && line.EndsWith(']')) { Flush(); name = line[1..^1]; continue; }
            var eq = line.IndexOf('=');
            if (eq > 0 && name != null) fields.Add((line[..eq], line[(eq + 1)..]));
        }
        Flush();
        return list;
    }

    // ---------- Variables y comodines ----------
    private static string Var(string name) => name.ToLowerInvariant() switch
    {
        "appdata" => Util.RoamingAppData,
        "localappdata" => Util.LocalAppData,
        "locallowappdata" => Path.Combine(Util.UserProfile, @"AppData\LocalLow"),
        "commonappdata" or "programdata" or "allusersprofile" => Util.ProgramData,
        "userprofile" => Util.UserProfile,
        "documents" => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "pictures" => Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        "music" => Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
        "video" => Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
        "desktop" => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        "public" => Util.Env("PUBLIC"),
        "windir" or "systemroot" => Util.WinDir,
        "systemdrive" => (Path.GetPathRoot(Util.WinDir) ?? @"C:\").TrimEnd('\\'),
        "commonprogramfiles" => Util.Env("CommonProgramFiles"),
        "temp" or "tmp" => Path.GetTempPath().TrimEnd('\\'),
        _ => Util.Env(name),
    };

    /// <summary>Expande %Variable% y los comodines (*) de cada tramo de la ruta. %ProgramFiles% cubre las dos carpetas.</summary>
    public static IEnumerable<string> ExpandDirs(string path)
    {
        var bases = new List<string>();
        if (path.Contains("%ProgramFiles%", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var pf in new[] { Util.Env("ProgramFiles"), Util.Env("ProgramFiles(x86)") }.Where(p => p.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
                bases.Add(Regex.Replace(path, "%ProgramFiles%", pf.Replace("$", "$$"), RegexOptions.IgnoreCase));
        }
        else bases.Add(path);

        foreach (var b in bases)
        {
            var expanded = VarRegex().Replace(b, m => Var(m.Groups[1].Value) is { Length: > 0 } v ? v : m.Value);
            if (expanded.Contains('%')) continue;   // variable desconocida: no arriesgar
            foreach (var d in Wildcards(expanded.TrimEnd('\\'))) yield return d;
        }
    }

    private static IEnumerable<string> Wildcards(string path)
    {
        if (!path.Contains('*') && !path.Contains('?')) { if (Directory.Exists(path)) yield return path; yield break; }
        var parts = path.Split('\\');
        var current = new List<string> { parts[0] + "\\" };
        for (int i = 1; i < parts.Length && current.Count > 0; i++)
        {
            var part = parts[i];
            var next = new List<string>();
            foreach (var dir in current)
            {
                if (part.Contains('*') || part.Contains('?'))
                {
                    foreach (var d in Util.SafeDirs(dir))
                        if (WildcardMatch(d.Name, part)) next.Add(d.FullName);
                }
                else
                {
                    var p = Path.Combine(dir, part);
                    if (Directory.Exists(p)) next.Add(p);
                }
            }
            current = next.Count > 200 ? next.Take(200).ToList() : next;   // límite de seguridad
        }
        foreach (var d in current) yield return d;
    }

    public static bool WildcardMatch(string text, string pattern) =>
        Regex.IsMatch(text, "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$", RegexOptions.IgnoreCase);

    // ---------- Detección ----------
    private static bool IsInstalled(Entry e)
    {
        if (e.Detect.Count == 0 && e.DetectFile.Count == 0) return false;   // sin forma de saberlo: no se muestra
        foreach (var d in e.Detect)
        {
            var parts = d.Split('\\', 2);
            if (parts.Length < 2) continue;
            var hive = parts[0].ToUpperInvariant() switch { "HKCU" => RegistryHive.CurrentUser, "HKLM" => RegistryHive.LocalMachine, "HKCR" => RegistryHive.ClassesRoot, _ => (RegistryHive?)null };
            if (hive == null) continue;
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using var k = RegistryKey.OpenBaseKey(hive.Value, view).OpenSubKey(parts[1]);
                    if (k != null) return true;
                }
                catch { }
            }
        }
        foreach (var f in e.DetectFile)
        {
            if (ExpandDirs(f).Any()) return true;
            // DetectFile también puede apuntar a un archivo
            var dir = Path.GetDirectoryName(f);
            var file = Path.GetFileName(f);
            if (dir != null && ExpandDirs(dir).Any(d => Util.SafeFiles(d, file, false).Any())) return true;
        }
        return false;
    }

    // ---------- Conversión a reglas de OpenSweep ----------
    public static List<CleanRule> BuildRules()
    {
        var rules = new List<CleanRule>();
        foreach (var e in Parse())
        {
            bool installed;
            try { installed = IsInstalled(e); } catch { installed = false; }
            if (!installed) continue;
            var entry = e;
            rules.Add(new CleanRule
            {
                Id = "w2." + e.Name,
                Group = Group,
                Section = e.Category,
                Name = e.Name,
                DefaultOn = false,
                Warning = e.Warning,
                Paths = () => Specs(entry),
                Exclude = path => IsExcluded(entry, path),
            });
        }
        return rules;
    }

    /// <summary>FileKeyN=ruta|patrón1;patrón2|RECURSE o REMOVESELF</summary>
    internal static IEnumerable<PathSpec> Specs(Entry e)
    {
        foreach (var fk in e.FileKeys)
        {
            var p = fk.Split('|');
            var patterns = (p.Length > 1 && p[1].Length > 0 ? p[1] : "*").Split(';', StringSplitOptions.RemoveEmptyEntries);
            var flag = p.Length > 2 ? p[2].ToUpperInvariant() : "";
            bool recurse = flag is "RECURSE" or "REMOVESELF";
            bool removeSelf = flag == "REMOVESELF";
            bool everything = patterns.Any(x => x is "*" or "*.*");
            // Seguridad: un comodín justo debajo de una carpeta protegida (p. ej. «%LocalAppData%\*») alcanzaría
            // las carpetas de TODOS los programas: esas reglas se descartan si borran mucho
            if (WildcardUnderProtected(p[0]) && (recurse || everything || removeSelf)) continue;
            foreach (var dir in ExpandDirs(p[0]))
            {
                // Seguridad: nunca tocar directamente una carpeta del sistema o del usuario (Documentos, Imágenes…)
                if (PathHelper.IsProtectedFolder(dir)) continue;
                foreach (var pattern in patterns)
                    yield return new PathSpec(dir, pattern, recurse, 0, recurse, removeSelf);
            }
        }
    }

    /// <summary>¿El primer tramo con comodín de la ruta está justo debajo de una carpeta protegida?</summary>
    internal static bool WildcardUnderProtected(string path)
    {
        var parts = path.TrimEnd('\\').Split('\\');
        var i = Array.FindIndex(parts, x => x.Contains('*') || x.Contains('?'));
        if (i <= 0) return false;
        var prefix = string.Join("\\", parts[..i]);
        return ExpandDirs(prefix).Any(PathHelper.IsProtectedFolder);
    }

    /// <summary>ExcludeKeyN=FILE|carpeta\|archivos  o  PATH|carpeta\  o  PATH|carpeta\|patrón</summary>
    internal static bool IsExcluded(Entry e, string file)
    {
        foreach (var ex in e.ExcludeKeys)
        {
            var p = ex.Split('|');
            if (p.Length < 2) continue;
            var name = Path.GetFileName(file);
            foreach (var dir in ExpandDirs(p[1]))
            {
                var prefix = dir.TrimEnd('\\') + "\\";
                if (p[0].Equals("FILE", StringComparison.OrdinalIgnoreCase))
                {
                    if (Path.GetDirectoryName(file)?.TrimEnd('\\').Equals(dir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) == true &&
                        (p.Length < 3 || p[2].Split(';').Any(x => WildcardMatch(name, x)))) return true;
                }
                else if (file.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                         (p.Length < 3 || p[2].Split(';').Any(x => WildcardMatch(name, x)))) return true;
            }
        }
        return false;
    }

    [GeneratedRegex("%([A-Za-z0-9()]+)%")]
    private static partial Regex VarRegex();
}
