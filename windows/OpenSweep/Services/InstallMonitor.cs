using System.Text.Json;
using OpenSweep.Core;
using Microsoft.Win32;

namespace OpenSweep.Services;

public sealed class TraceItem
{
    public string Kind { get; set; } = "";       // dir | file | key | value
    public string Path { get; set; } = "";
    public string Hive { get; set; } = "";       // HKLM | HKCU
    public string View { get; set; } = "";       // 64 | 32
    public string? ValueName { get; set; }
}

public sealed class InstallTrace
{
    public string Name { get; set; } = "";
    public string Installer { get; set; } = "";
    public DateTime Date { get; set; }
    public string? UninstallKey { get; set; }     // clave de desinstalación creada (si la hay)
    public string? UninstallView { get; set; }
    public string? UninstallHive { get; set; }
    public List<TraceItem> Items { get; set; } = [];

    public string FileName { get; set; } = "";
    public string DateText => Date.ToString("dd/MM/yyyy HH:mm");
    public int Count => Items.Count;
}

/// <summary>Instantánea del sistema antes y después de instalar un programa.</summary>
public sealed class Snapshot
{
    public HashSet<string> Dirs { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Keys { get; } = new(StringComparer.OrdinalIgnoreCase);    // "HKLM|64|SOFTWARE\X"
    public HashSet<string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);  // "HKLM|64|path|name"
}

public static class InstallMonitor
{
    public static string Folder => Path.Combine(AppSettings.Folder, "Instalaciones");

    private static readonly string[] Noise =
    [
        @"\Temp\", @"\INetCache\", @"\Google\Chrome\User Data\", @"\Microsoft\Edge\User Data\", @"\BraveSoftware\",
        @"\Mozilla\Firefox\", @"\D3DSCache\", @"\CrashDumps\", @"\Microsoft\Windows\WER\", @"\npm-cache\", @"\NuGet\",
        @"\Microsoft\Windows\Explorer\", @"\Packages\", @"\OpenSweep\", @"\Microsoft\Windows\INetCookies\", @"\Microsoft\Windows\WebCache\",
    ];

    private static bool IsNoise(string path) => Noise.Any(n => path.Contains(n, StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<string> FileRoots() => new[]
    {
        Util.Env("ProgramFiles"), Util.Env(Loc.T("ProgramFiles(x86)")), Util.ProgramData, Util.LocalAppData, Util.RoamingAppData,
        Path.Combine(Util.UserProfile, @"AppData\LocalLow"),
    }.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<string> ShortcutRoots() => new[]
    {
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
        Environment.GetFolderPath(Environment.SpecialFolder.Programs),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
        Environment.GetFolderPath(Environment.SpecialFolder.Startup),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup),
    }.Where(Directory.Exists);

    private static readonly (RegistryHive hive, RegistryView view, string path, int depth)[] RegRoots =
    [
        (RegistryHive.LocalMachine, RegistryView.Registry64, "SOFTWARE", 3),
        (RegistryHive.LocalMachine, RegistryView.Registry32, "SOFTWARE", 3),
        (RegistryHive.CurrentUser, RegistryView.Registry64, "Software", 3),
        (RegistryHive.LocalMachine, RegistryView.Registry64, @"SYSTEM\CurrentControlSet\Services", 1),
        (RegistryHive.LocalMachine, RegistryView.Registry64, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", 1),
        (RegistryHive.LocalMachine, RegistryView.Registry32, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", 1),
        (RegistryHive.CurrentUser, RegistryView.Registry64, @"Software\Microsoft\Windows\CurrentVersion\Uninstall", 1),
        (RegistryHive.LocalMachine, RegistryView.Registry64, @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths", 1),
        (RegistryHive.CurrentUser, RegistryView.Registry64, @"Software\Classes", 1),
    ];

    private static readonly (RegistryHive hive, RegistryView view, string path)[] ValueRoots =
    [
        (RegistryHive.CurrentUser, RegistryView.Registry64, @"Software\Microsoft\Windows\CurrentVersion\Run"),
        (RegistryHive.LocalMachine, RegistryView.Registry64, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"),
        (RegistryHive.LocalMachine, RegistryView.Registry32, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"),
    ];

    private static string H(RegistryHive h) => h == RegistryHive.CurrentUser ? "HKCU" : "HKLM";
    private static string V(RegistryView v) => v == RegistryView.Registry32 ? "32" : "64";

    public static Snapshot Take(IProgress<string> progress)
    {
        var s = new Snapshot();
        foreach (var root in FileRoots())
        {
            progress.Report(Loc.T("Registrando carpetas de") + " " + root);
            foreach (var d in Util.SafeDirs(root, recursive: true))
                if (!IsNoise(d.FullName + "\\")) s.Dirs.Add(d.FullName);
            foreach (var f in Util.SafeFiles(root, "*", false)) s.Files.Add(f.FullName);
        }
        foreach (var root in ShortcutRoots())
            foreach (var f in Util.SafeFiles(root)) s.Files.Add(f.FullName);

        progress.Report(Loc.T("Registrando el registro de Windows…"));
        foreach (var (hive, view, path, depth) in RegRoots)
        {
            try
            {
                using var k = RegistryKey.OpenBaseKey(hive, view).OpenSubKey(path);
                if (k != null) WalkKeys(k, $"{H(hive)}|{V(view)}|{path}", depth, s.Keys);
            }
            catch { }
        }
        foreach (var (hive, view, path) in ValueRoots)
        {
            try
            {
                using var k = RegistryKey.OpenBaseKey(hive, view).OpenSubKey(path);
                if (k != null) foreach (var v in k.GetValueNames()) s.Values.Add($"{H(hive)}|{V(view)}|{path}|{v}");
            }
            catch { }
        }
        return s;
    }

    private static void WalkKeys(RegistryKey key, string id, int depth, HashSet<string> set)
    {
        if (depth <= 0) return;
        string[] names;
        try { names = key.GetSubKeyNames(); } catch { return; }
        foreach (var n in names)
        {
            var childId = $"{id}\\{n}";
            set.Add(childId);
            if (depth > 1)
            {
                try
                {
                    using var c = key.OpenSubKey(n);
                    if (c != null) WalkKeys(c, childId, depth - 1, set);
                }
                catch { }
            }
        }
    }

    /// <summary>Compara dos instantáneas y devuelve solo los elementos nuevos de nivel superior.</summary>
    public static InstallTrace Diff(Snapshot before, Snapshot after, string installer)
    {
        var trace = new InstallTrace { Installer = installer, Date = DateTime.Now, Name = Path.GetFileNameWithoutExtension(installer) };

        var newDirs = after.Dirs.Except(before.Dirs, StringComparer.OrdinalIgnoreCase).OrderBy(d => d.Length).ToList();
        var topDirs = new List<string>();
        foreach (var d in newDirs)
            if (!topDirs.Any(t => d.StartsWith(t + "\\", StringComparison.OrdinalIgnoreCase))) topDirs.Add(d);
        trace.Items.AddRange(topDirs.Select(d => new TraceItem { Kind = "dir", Path = d }));

        foreach (var f in after.Files.Except(before.Files, StringComparer.OrdinalIgnoreCase))
            if (!topDirs.Any(t => f.StartsWith(t + "\\", StringComparison.OrdinalIgnoreCase)) && !IsNoise(f))
                trace.Items.Add(new TraceItem { Kind = "file", Path = f });

        var newKeys = after.Keys.Except(before.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(k => k.Length).ToList();
        var topKeys = new List<string>();
        foreach (var k in newKeys)
            if (!topKeys.Any(t => k.StartsWith(t + "\\", StringComparison.OrdinalIgnoreCase))) topKeys.Add(k);
        foreach (var k in topKeys)
        {
            var p = k.Split('|');
            trace.Items.Add(new TraceItem { Kind = "key", Hive = p[0], View = p[1], Path = p[2] });
            if (p[2].Contains(@"\CurrentVersion\Uninstall\", StringComparison.OrdinalIgnoreCase))
            {
                trace.UninstallHive = p[0]; trace.UninstallView = p[1]; trace.UninstallKey = p[2];
                try
                {
                    using var uk = RegistryKey.OpenBaseKey(p[0] == "HKCU" ? RegistryHive.CurrentUser : RegistryHive.LocalMachine,
                        p[1] == "32" ? RegistryView.Registry32 : RegistryView.Registry64).OpenSubKey(p[2]);
                    if (uk?.GetValue("DisplayName") is string dn && dn.Length > 0) trace.Name = dn;
                }
                catch { }
            }
        }
        foreach (var v in after.Values.Except(before.Values, StringComparer.OrdinalIgnoreCase))
        {
            var p = v.Split('|');
            trace.Items.Add(new TraceItem { Kind = "value", Hive = p[0], View = p[1], Path = p[2], ValueName = p[3] });
        }
        return trace;
    }

    public static void Save(InstallTrace t)
    {
        Directory.CreateDirectory(Folder);
        var safe = string.Concat(t.Name.Where(c => !Path.GetInvalidFileNameChars().Contains(c)));
        t.FileName = Path.Combine(Folder, $"{t.Date:yyyyMMdd_HHmmss}_{safe}.json");
        File.WriteAllText(t.FileName, JsonSerializer.Serialize(t, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static List<InstallTrace> LoadAll()
    {
        var list = new List<InstallTrace>();
        foreach (var f in Util.SafeFiles(Folder, "*.json", false))
        {
            try
            {
                var t = JsonSerializer.Deserialize<InstallTrace>(File.ReadAllText(f.FullName));
                if (t != null) { t.FileName = f.FullName; list.Add(t); }
            }
            catch { }
        }
        return list.OrderByDescending(t => t.Date).ToList();
    }

    /// <summary>Elementos del registro de instalación que todavía existen, como lista de restos.</summary>
    public static List<Leftover> RemainingItems(InstallTrace t)
    {
        var list = new List<Leftover>();
        foreach (var i in t.Items)
        {
            var hive = i.Hive == "HKCU" ? RegistryHive.CurrentUser : RegistryHive.LocalMachine;
            var view = i.View == "32" ? RegistryView.Registry32 : RegistryView.Registry64;
            switch (i.Kind)
            {
                case "dir" when Directory.Exists(i.Path) && !PathHelper.IsProtectedFolder(i.Path):
                    list.Add(new Leftover { Path = i.Path, IsRegistry = false, IsDirectory = true, Size = Util.DirSize(i.Path), Reason = Loc.T("Creado por el instalador") });
                    break;
                case "file" when File.Exists(i.Path):
                    list.Add(new Leftover { Path = i.Path, IsRegistry = false, Size = new FileInfo(i.Path).Length, Reason = Loc.T("Creado por el instalador") });
                    break;
                case "key":
                    try
                    {
                        using var k = RegistryKey.OpenBaseKey(hive, view).OpenSubKey(i.Path);
                        if (k != null) list.Add(new Leftover { Path = i.Path, IsRegistry = true, Hive = hive, View = view, Reason = Loc.T("Creado por el instalador") });
                    }
                    catch { }
                    break;
                case "value":
                    try
                    {
                        using var k = RegistryKey.OpenBaseKey(hive, view).OpenSubKey(i.Path);
                        if (k?.GetValue(i.ValueName) != null)
                            list.Add(new Leftover { Path = i.Path, IsRegistry = true, Hive = hive, View = view, ValueName = i.ValueName, Reason = Loc.T("Creado por el instalador") });
                    }
                    catch { }
                    break;
            }
        }
        return list;
    }

    /// <summary>Programa instalado asociado al registro (si su clave de desinstalación sigue existiendo).</summary>
    public static InstalledProgram? Program(InstallTrace t)
    {
        if (t.UninstallKey == null) return null;
        var keyName = t.UninstallKey[(t.UninstallKey.LastIndexOf('\\') + 1)..];
        return UninstallerService.GetInstalledPrograms(includeSystem: true).FirstOrDefault(p =>
            p.KeyName.Equals(keyName, StringComparison.OrdinalIgnoreCase) && (p.Hive == RegistryHive.CurrentUser) == (t.UninstallHive == "HKCU"));
    }
}
