using System.Text;
using OpenSweep.Core;
using Microsoft.Win32;

namespace OpenSweep.Services;

public sealed class RegistryIssue
{
    public required string Category { get; init; }
    public required RegistryHive Hive { get; init; }
    public required RegistryView View { get; init; }
    public required string KeyPath { get; init; }       // sin la raíz
    public string? ValueName { get; init; }              // null = eliminar la clave completa
    public required string Problem { get; init; }
    public string Data { get; init; } = "";
    public bool Selected { get; set; } = true;

    public string HiveName => Hive switch
    {
        RegistryHive.LocalMachine => "HKEY_LOCAL_MACHINE",
        RegistryHive.CurrentUser => "HKEY_CURRENT_USER",
        RegistryHive.ClassesRoot => "HKEY_CLASSES_ROOT",
        _ => Hive.ToString()
    };
    public string FullKey => $"{HiveName}\\{KeyPath}";
    public string CategoryText => Loc.T(Category);
    public string ProblemText => Problem.Contains(':') ? Loc.T(Problem[..Problem.IndexOf(':')]) + Problem[Problem.IndexOf(':')..]
                               : Problem.Contains(" (") ? Loc.T(Problem[..Problem.IndexOf(" (")]) + Problem[Problem.IndexOf(" (")..] : Loc.T(Problem);
    public string Entry => ValueName == null ? FullKey : $"{FullKey}  →  {(ValueName.Length == 0 ? "(Predeterminado)" : ValueName)}";
}

public sealed record RegistryCategory(string Name, bool DefaultOn = true)
{
    public bool Selected { get; set; } = DefaultOn;
    public string NameText => Loc.T(Name);
}

public static class RegistryCleaner
{
    public static List<RegistryCategory> Categories() =>
    [
        new("DLL compartidas faltantes"),
        new("Programas desinstalados (entradas huérfanas)"),
        new("Rutas de aplicaciones"),
        new("Aplicaciones registradas sin ejecutable"),
        new("Caché MUI de aplicaciones"),
        new("Programas de inicio inválidos"),
        new("Carpetas del instalador"),
        new("Archivos de ayuda"),
        new("Extensiones de archivo sin asociación", false),
    ];

    public static List<RegistryIssue> Scan(IEnumerable<string> categories, IProgress<string>? progress, CancellationToken ct)
    {
        var set = categories.ToHashSet();
        var issues = new List<RegistryIssue>();
        void Step(string name, Action a)
        {
            if (!set.Contains(name)) return;
            ct.ThrowIfCancellationRequested();
            progress?.Report(name);
            try { a(); } catch { }
        }

        Step("DLL compartidas faltantes", () =>
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                const string path = @"SOFTWARE\Microsoft\Windows\CurrentVersion\SharedDLLs";
                using var k = Open(RegistryHive.LocalMachine, view, path);
                if (k == null) continue;
                foreach (var v in k.GetValueNames())
                {
                    if (!PathHelper.FileExistsSmart(v))
                        issues.Add(new RegistryIssue { Category = "DLL compartidas faltantes", Hive = RegistryHive.LocalMachine, View = view, KeyPath = path, ValueName = v, Problem = "Archivo DLL inexistente", Data = v });
                }
            }
        });

        Step("Programas desinstalados (entradas huérfanas)", () =>
        {
            foreach (var (hive, view) in new[] { (RegistryHive.LocalMachine, RegistryView.Registry64), (RegistryHive.LocalMachine, RegistryView.Registry32), (RegistryHive.CurrentUser, RegistryView.Registry64) })
            {
                const string path = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
                using var k = Open(hive, view, path);
                if (k == null) continue;
                foreach (var sub in k.GetSubKeyNames())
                {
                    using var sk = k.OpenSubKey(sub);
                    if (sk == null) continue;
                    var name = sk.GetValue("DisplayName") as string;
                    var uninst = sk.GetValue("UninstallString") as string;
                    var loc = sk.GetValue("InstallLocation") as string;
                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(uninst)) continue;
                    if (uninst.Contains("msiexec", StringComparison.OrdinalIgnoreCase)) continue; // MSI: lo gestiona Windows Installer
                    var exe = PathHelper.ExtractPath(uninst);
                    bool exeMissing = exe != null && Path.IsPathRooted(exe) && !PathHelper.FileExistsSmart(exe);
                    bool locMissing = string.IsNullOrWhiteSpace(loc) || !Directory.Exists(Environment.ExpandEnvironmentVariables(loc.Trim('"')));
                    if (exeMissing && locMissing)
                        issues.Add(new RegistryIssue { Category = "Programas desinstalados (entradas huérfanas)", Hive = hive, View = view, KeyPath = $"{path}\\{sub}", Problem = $"Desinstalador inexistente: {name}", Data = uninst });
                }
            }
        });

        Step("Rutas de aplicaciones", () =>
        {
            foreach (var (hive, view) in new[] { (RegistryHive.LocalMachine, RegistryView.Registry64), (RegistryHive.LocalMachine, RegistryView.Registry32), (RegistryHive.CurrentUser, RegistryView.Registry64) })
            {
                const string path = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths";
                using var k = Open(hive, view, path);
                if (k == null) continue;
                foreach (var sub in k.GetSubKeyNames())
                {
                    using var sk = k.OpenSubKey(sub);
                    var val = sk?.GetValue("") as string;
                    if (string.IsNullOrWhiteSpace(val)) continue;
                    var exe = PathHelper.ExtractPath(val);
                    if (exe != null && Path.IsPathRooted(exe) && !PathHelper.FileExistsSmart(exe))
                        issues.Add(new RegistryIssue { Category = "Rutas de aplicaciones", Hive = hive, View = view, KeyPath = $"{path}\\{sub}", Problem = "Ruta de aplicación inválida", Data = val });
                }
            }
        });

        Step("Aplicaciones registradas sin ejecutable", () =>
        {
            const string path = @"Software\Classes\Applications";
            using var k = Open(RegistryHive.CurrentUser, RegistryView.Registry64, path);
            if (k == null) return;
            foreach (var sub in k.GetSubKeyNames())
            {
                using var cmd = k.OpenSubKey($@"{sub}\shell\open\command");
                var val = cmd?.GetValue("") as string;
                if (string.IsNullOrWhiteSpace(val)) continue;
                var exe = PathHelper.ExtractPath(val);
                if (exe != null && Path.IsPathRooted(exe) && !PathHelper.FileExistsSmart(exe))
                    issues.Add(new RegistryIssue { Category = "Aplicaciones registradas sin ejecutable", Hive = RegistryHive.CurrentUser, View = RegistryView.Registry64, KeyPath = $"{path}\\{sub}", Problem = "Aplicación inexistente", Data = val });
            }
        });

        Step("Caché MUI de aplicaciones", () =>
        {
            const string path = @"Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\MuiCache";
            using var k = Open(RegistryHive.CurrentUser, RegistryView.Registry64, path);
            if (k == null) return;
            foreach (var v in k.GetValueNames())
            {
                var dot = v.LastIndexOf('.');
                if (dot <= 0) continue;
                var file = v[..dot];
                if (!file.Contains('\\') || file.StartsWith('@')) continue;
                if (!PathHelper.FileExistsSmart(file))
                    issues.Add(new RegistryIssue { Category = "Caché MUI de aplicaciones", Hive = RegistryHive.CurrentUser, View = RegistryView.Registry64, KeyPath = path, ValueName = v, Problem = "Referencia a programa inexistente", Data = file });
            }
        });

        Step("Programas de inicio inválidos", () =>
        {
            foreach (var (hive, view, path) in new[]
            {
                (RegistryHive.CurrentUser, RegistryView.Registry64, @"Software\Microsoft\Windows\CurrentVersion\Run"),
                (RegistryHive.LocalMachine, RegistryView.Registry64, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"),
                (RegistryHive.LocalMachine, RegistryView.Registry32, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"),
            })
            {
                using var k = Open(hive, view, path);
                if (k == null) continue;
                foreach (var v in k.GetValueNames())
                {
                    var val = k.GetValue(v) as string;
                    var exe = PathHelper.ExtractPath(val);
                    if (exe != null && Path.IsPathRooted(exe) && !PathHelper.FileExistsSmart(exe))
                        issues.Add(new RegistryIssue { Category = "Programas de inicio inválidos", Hive = hive, View = view, KeyPath = path, ValueName = v, Problem = "Programa de inicio inexistente", Data = val ?? "" });
                }
            }
        });

        Step("Carpetas del instalador", () =>
        {
            const string path = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Installer\Folders";
            using var k = Open(RegistryHive.LocalMachine, RegistryView.Registry64, path);
            if (k == null) return;
            foreach (var v in k.GetValueNames())
            {
                if (!Path.IsPathRooted(v)) continue;
                var root = Path.GetPathRoot(v);
                if (root != null && !Directory.Exists(root)) continue;
                if (!Directory.Exists(v))
                    issues.Add(new RegistryIssue { Category = "Carpetas del instalador", Hive = RegistryHive.LocalMachine, View = RegistryView.Registry64, KeyPath = path, ValueName = v, Problem = "Carpeta inexistente", Data = v });
            }
        });

        Step("Archivos de ayuda", () =>
        {
            foreach (var path in new[] { @"SOFTWARE\Microsoft\Windows\HTML Help", @"SOFTWARE\Microsoft\Windows\Help" })
            {
                foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
                {
                    using var k = Open(RegistryHive.LocalMachine, view, path);
                    if (k == null) continue;
                    foreach (var v in k.GetValueNames())
                    {
                        if (v.Length == 0) continue;
                        var dir = k.GetValue(v) as string;
                        if (string.IsNullOrWhiteSpace(dir)) continue;
                        var full = Path.Combine(Environment.ExpandEnvironmentVariables(dir), v);
                        if (!PathHelper.FileExistsSmart(full))
                            issues.Add(new RegistryIssue { Category = "Archivos de ayuda", Hive = RegistryHive.LocalMachine, View = view, KeyPath = path, ValueName = v, Problem = "Archivo de ayuda inexistente", Data = full });
                    }
                }
            }
        });

        Step("Extensiones de archivo sin asociación", () =>
        {
            const string path = @"Software\Classes";
            using var k = Open(RegistryHive.CurrentUser, RegistryView.Registry64, path);
            if (k == null) return;
            using var hkcr = Open(RegistryHive.ClassesRoot, RegistryView.Registry64, "");
            foreach (var sub in k.GetSubKeyNames().Where(n => n.StartsWith('.')))
            {
                using var ext = k.OpenSubKey(sub);
                if (ext == null) continue;
                if (ext.SubKeyCount > 0 || ext.ValueCount != 1) continue;
                var progId = ext.GetValue("") as string;
                if (string.IsNullOrWhiteSpace(progId)) continue;
                using var pk = hkcr?.OpenSubKey(progId);
                if (pk == null)
                    issues.Add(new RegistryIssue { Category = "Extensiones de archivo sin asociación", Hive = RegistryHive.CurrentUser, View = RegistryView.Registry64, KeyPath = $"{path}\\{sub}", Problem = $"ProgID inexistente ({progId})", Data = progId });
            }
        });

        // Algunas claves son compartidas entre las vistas de 32 y 64 bits: quitar duplicados
        return issues.GroupBy(i => (i.FullKey.ToLowerInvariant(), i.ValueName?.ToLowerInvariant()))
                     .Select(g => g.First())
                     .ToList();
    }

    private static RegistryKey? Open(RegistryHive hive, RegistryView view, string path, bool writable = false)
    {
        try
        {
            var root = RegistryKey.OpenBaseKey(hive, view);
            return path.Length == 0 ? root : root.OpenSubKey(path, writable);
        }
        catch { return null; }
    }

    /// <summary>Exporta las claves afectadas a un archivo .reg para poder deshacer los cambios.</summary>
    public static string? Backup(IEnumerable<RegistryIssue> issues)
    {
        try
        {
            Directory.CreateDirectory(AppSettings.BackupFolder);
            var file = Path.Combine(AppSettings.BackupFolder, $"Registro_{DateTime.Now:yyyyMMdd_HHmmss}.reg");
            var sb = new StringBuilder("Windows Registry Editor Version 5.00\r\n\r\n");
            var tmp = Path.Combine(Path.GetTempPath(), "cf_regexp.reg");
            foreach (var g in issues.GroupBy(i => (i.FullKey, i.View)))
            {
                var first = g.First();
                // Si solo se borran VALORES, se guardan solo esos valores: exportar la clave entera haría que al
                // restaurar volviera todo lo demás tal como estaba (reglas o entradas de inicio quitadas después)
                if (g.All(i => i.ValueName != null))
                {
                    sb.Append(ValuesToReg(first.Hive, first.View, first.KeyPath, g.Select(i => i.ValueName!).Distinct()));
                    continue;
                }
                // Claves de 32 bits: se exporta su ruta real (WOW6432Node) para que «reg import» las devuelva a su sitio
                var (code, _) = Util.Run("reg.exe", $"export \"{RealKeyPath(first.Hive, first.View, first.KeyPath)}\" \"{tmp}\" /y /reg:64", 60_000);
                if (code != 0 || !File.Exists(tmp)) continue;
                var text = File.ReadAllText(tmp, Encoding.Unicode);
                var idx = text.IndexOf('\n');
                sb.Append(idx >= 0 ? text[(idx + 1)..] : text).Append("\r\n");
            }
            try { File.Delete(tmp); } catch { }
            File.WriteAllText(file, sb.ToString(), Encoding.Unicode);
            return file;
        }
        catch { return null; }
    }

    /// <summary>Ruta completa tal como la ve la vista de 64 bits (las claves de 32 bits de HKLM\SOFTWARE están en WOW6432Node).</summary>
    internal static string RealKeyPath(RegistryHive hive, RegistryView view, string keyPath)
    {
        var root = hive == RegistryHive.CurrentUser ? "HKEY_CURRENT_USER" : hive == RegistryHive.ClassesRoot ? "HKEY_CLASSES_ROOT" : "HKEY_LOCAL_MACHINE";
        if (view == RegistryView.Registry32 && hive == RegistryHive.LocalMachine && Environment.Is64BitOperatingSystem
            && keyPath.StartsWith(@"SOFTWARE\", StringComparison.OrdinalIgnoreCase)
            && !keyPath.StartsWith(@"SOFTWARE\WOW6432Node", StringComparison.OrdinalIgnoreCase))
            keyPath = @"SOFTWARE\WOW6432Node\" + keyPath[9..];
        return $@"{root}\{keyPath}";
    }

    /// <summary>Bloque .reg con solo los valores indicados de una clave (con su tipo exacto).</summary>
    internal static string ValuesToReg(RegistryHive hive, RegistryView view, string keyPath, IEnumerable<string> names)
    {
        var sb = new StringBuilder();
        sb.Append('[').Append(RealKeyPath(hive, view, keyPath)).Append("]\r\n");
        try
        {
            using var k = RegistryKey.OpenBaseKey(hive, view).OpenSubKey(keyPath);
            if (k != null)
                foreach (var n in names)
                {
                    var data = k.GetValue(n, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                    if (data == null) continue;
                    var name = n.Length == 0 ? "@" : $"\"{Escape(n)}\"";
                    sb.Append(name).Append('=').Append(FormatValue(k.GetValueKind(n), data)).Append("\r\n");
                }
        }
        catch { }
        return sb.Append("\r\n").ToString();
    }

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static string Hex(IEnumerable<byte> bytes) => string.Join(",", bytes.Select(b => b.ToString("x2")));

    internal static string FormatValue(RegistryValueKind kind, object data) => kind switch
    {
        RegistryValueKind.String => $"\"{Escape((string)data)}\"",
        RegistryValueKind.DWord => $"dword:{unchecked((uint)(int)data):x8}",
        RegistryValueKind.QWord => "hex(b):" + Hex(BitConverter.GetBytes((long)data)),
        RegistryValueKind.ExpandString => "hex(2):" + Hex(Encoding.Unicode.GetBytes((string)data + "\0")),
        RegistryValueKind.MultiString => "hex(7):" + Hex(Encoding.Unicode.GetBytes(string.Concat(((string[])data).Select(s => s + "\0")) + "\0")),
        RegistryValueKind.Binary => "hex:" + Hex((byte[])data),
        _ => "hex:" + Hex(data as byte[] ?? []),
    };

    public static (int fixedCount, int failed) Fix(IEnumerable<RegistryIssue> issues)
    {
        int ok = 0, fail = 0;
        foreach (var i in issues)
        {
            try
            {
                if (i.ValueName != null)
                {
                    using var k = Open(i.Hive, i.View, i.KeyPath, true);
                    if (k == null) { fail++; continue; }
                    k.DeleteValue(i.ValueName, false);
                }
                else
                {
                    var parentPath = i.KeyPath[..i.KeyPath.LastIndexOf('\\')];
                    var name = i.KeyPath[(i.KeyPath.LastIndexOf('\\') + 1)..];
                    using var parent = Open(i.Hive, i.View, parentPath, true);
                    if (parent == null) { fail++; continue; }
                    parent.DeleteSubKeyTree(name, false);
                }
                ok++;
            }
            catch { fail++; }
        }
        return (ok, fail);
    }
}
