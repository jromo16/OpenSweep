using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Windows.Media;
using OpenSweep.Core;
using Microsoft.Win32;

namespace OpenSweep.Services;

public sealed class InstalledProgram
{
    public required string Name { get; init; }
    public string Publisher { get; init; } = "";
    public string Version { get; init; } = "";
    public DateTime? InstallDate { get; init; }
    public long SizeBytes { get; set; }
    public string InstallLocation { get; init; } = "";
    public string UninstallString { get; init; } = "";
    public string QuietUninstallString { get; init; } = "";
    public string DisplayIcon { get; init; } = "";
    public required RegistryHive Hive { get; init; }
    public required RegistryView View { get; init; }
    public required string KeyName { get; init; }
    public bool IsMsi { get; init; }
    public bool IsSystem { get; init; }
    public ImageSource? Icon { get; set; }

    public string KeyPath => $@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{KeyName}";
    public string SizeText => SizeBytes > 0 ? Util.FormatBytes(SizeBytes) : "";
    public string DateText => InstallDate?.ToString("dd/MM/yyyy") ?? "";
    public string Arch => View == RegistryView.Registry32 && Environment.Is64BitOperatingSystem ? "32 bits" : "64 bits";
    public string Scope => Hive == RegistryHive.CurrentUser ? "Usuario actual" : "Todos los usuarios";
}

public sealed class Leftover
{
    public required string Path { get; init; }
    public required bool IsRegistry { get; init; }
    public bool IsDirectory { get; init; }
    public RegistryHive Hive { get; init; }
    public RegistryView View { get; init; }
    public string? ValueName { get; init; }
    public long Size { get; init; }
    public string Reason { get; init; } = "";
    public bool Selected { get; set; } = true;

    public string Kind => IsRegistry ? (ValueName != null ? "Valor del registro" : "Clave del registro") : IsDirectory ? "Carpeta" : "Archivo";
    public string Display => IsRegistry
        ? (Hive == RegistryHive.CurrentUser ? "HKCU\\" : "HKLM\\") + Path + (ValueName != null ? $"  →  {ValueName}" : "") + (View == RegistryView.Registry32 ? "  (32 bits)" : "")
        : Path;
    public string SizeText => Size > 0 ? Util.FormatBytes(Size) : "";
}

public enum ScanMode { Seguro, Moderado, Avanzado }

public sealed class StoreApp
{
    public string Name { get; set; } = "";
    public string PackageFullName { get; set; } = "";
    public string Publisher { get; set; } = "";
    public string Version { get; set; } = "";
    public string InstallLocation { get; set; } = "";
    public bool Selected { get; set; }
}

public static class UninstallerService
{
    private const string UninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    public static List<InstalledProgram> GetInstalledPrograms(bool includeSystem = false)
    {
        var list = new List<InstalledProgram>();
        var sources = new List<(RegistryHive, RegistryView)> { (RegistryHive.LocalMachine, RegistryView.Registry64), (RegistryHive.CurrentUser, RegistryView.Registry64) };
        if (Environment.Is64BitOperatingSystem) sources.Insert(1, (RegistryHive.LocalMachine, RegistryView.Registry32));

        foreach (var (hive, view) in sources)
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var k = root.OpenSubKey(UninstallPath);
                if (k == null) continue;
                foreach (var sub in k.GetSubKeyNames())
                {
                    try
                    {
                        using var sk = k.OpenSubKey(sub);
                        if (sk == null) continue;
                        var name = (sk.GetValue("DisplayName") as string)?.Trim();
                        if (string.IsNullOrWhiteSpace(name)) continue;
                        var uninst = sk.GetValue("UninstallString") as string ?? "";
                        bool system = Convert.ToInt32(sk.GetValue("SystemComponent") ?? 0) == 1
                                      || sk.GetValue("ParentKeyName") != null
                                      || (sk.GetValue("ReleaseType") as string ?? "").Contains("Update", StringComparison.OrdinalIgnoreCase);
                        if (string.IsNullOrWhiteSpace(uninst)) system = true;
                        if (system && !includeSystem) continue;

                        DateTime? date = null;
                        var ds = sk.GetValue("InstallDate") as string;
                        if (ds != null && DateTime.TryParseExact(ds, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) date = d;

                        long size = 0;
                        try { size = Convert.ToInt64(sk.GetValue("EstimatedSize") ?? 0) * 1024; } catch { }

                        list.Add(new InstalledProgram
                        {
                            Name = name,
                            Publisher = (sk.GetValue("Publisher") as string ?? "").Trim(),
                            Version = sk.GetValue("DisplayVersion") as string ?? "",
                            InstallDate = date,
                            SizeBytes = size,
                            InstallLocation = (sk.GetValue("InstallLocation") as string ?? "").Trim().Trim('"'),
                            UninstallString = uninst,
                            QuietUninstallString = sk.GetValue("QuietUninstallString") as string ?? "",
                            DisplayIcon = sk.GetValue("DisplayIcon") as string ?? "",
                            Hive = hive,
                            View = view,
                            KeyName = sub,
                            IsMsi = Convert.ToInt32(sk.GetValue("WindowsInstaller") ?? 0) == 1 || uninst.Contains("msiexec", StringComparison.OrdinalIgnoreCase),
                            IsSystem = system,
                        });
                    }
                    catch { }
                }
            }
            catch { }
        }

        // quitar duplicados (mismo nombre y versión)
        return list.GroupBy(p => (p.Name.ToLowerInvariant(), p.Version))
                   .Select(g => g.First())
                   .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
                   .ToList();
    }

    public static void LoadIcon(InstalledProgram p)
    {
        var icon = Native.GetIcon(p.DisplayIcon);
        if (icon == null)
        {
            var exe = PathHelper.ExtractPath(p.UninstallString);
            if (exe != null && !exe.Contains("msiexec", StringComparison.OrdinalIgnoreCase)) icon = Native.GetIcon(exe);
        }
        p.Icon = icon;
    }

    /// <summary>Crea un punto de restauración del sistema (puede tardar o estar desactivado).</summary>
    public static bool CreateRestorePoint(string description)
    {
        var (code, _) = Util.PowerShell(
            "Enable-ComputerRestore -Drive \"$env:SystemDrive\\\" -ErrorAction SilentlyContinue; " +
            $"Checkpoint-Computer -Description '{description.Replace("'", "")}' -RestorePointType APPLICATION_UNINSTALL -ErrorAction Stop", 300_000);
        return code == 0;
    }

    /// <summary>Ejecuta el desinstalador del programa y espera a que termine (incluidos procesos hijos conocidos).</summary>
    public static async Task<int> RunUninstallerAsync(InstalledProgram p, bool quiet)
    {
        var cmd = quiet && !string.IsNullOrWhiteSpace(p.QuietUninstallString) ? p.QuietUninstallString : p.UninstallString;
        string file, args;
        if (p.IsMsi)
        {
            var guidStart = cmd.IndexOf('{');
            var guidEnd = cmd.IndexOf('}');
            var guid = guidStart >= 0 && guidEnd > guidStart ? cmd[guidStart..(guidEnd + 1)] : p.KeyName;
            file = "msiexec.exe";
            args = $"/x {guid}" + (quiet ? " /qb" : "");
        }
        else
        {
            var exe = PathHelper.ExtractPath(cmd) ?? cmd;
            var trimmed = Environment.ExpandEnvironmentVariables(cmd.Trim());
            if (trimmed.StartsWith('"'))
            {
                var end = trimmed.IndexOf('"', 1);
                args = end > 0 ? trimmed[(end + 1)..].Trim() : "";
            }
            else
            {
                var idx = trimmed.IndexOf(exe, StringComparison.OrdinalIgnoreCase);
                args = idx >= 0 ? trimmed[(idx + exe.Length)..].Trim() : "";
            }
            file = exe;
            if (!File.Exists(file))
            {
                // Último recurso: dejar que cmd interprete la línea completa
                file = "cmd.exe";
                args = $"/c \"{cmd}\"";
            }
        }

        var start = DateTime.Now;
        using var proc = Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = true });
        if (proc == null) return -1;
        await proc.WaitForExitAsync();

        // Muchos desinstaladores (NSIS: Au_.exe, Un_A.exe, Inno: _iu14D2N.tmp) se copian a %TEMP% y relanzan.
        for (int i = 0; i < 600; i++)
        {
            bool running = Process.GetProcesses().Any(pr =>
            {
                try
                {
                    var n = pr.ProcessName.ToLowerInvariant();
                    return (n.StartsWith("au_") || n.StartsWith("un_a") || n.StartsWith("_iu") || n.Contains("uninst") || n.StartsWith("unins"))
                           && pr.StartTime >= start.AddSeconds(-2);
                }
                catch { return false; }
            });
            if (!running) break;
            await Task.Delay(1000);
        }
        return proc.ExitCode;
    }

    public static bool StillInstalled(InstalledProgram p)
    {
        try
        {
            using var root = RegistryKey.OpenBaseKey(p.Hive, p.View);
            using var k = root.OpenSubKey(p.KeyPath);
            return k != null;
        }
        catch { return false; }
    }

    // ===================== Búsqueda de restos =====================

    private static readonly HashSet<string> GenericWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "microsoft", "windows", "common", "commonfiles", "program", "programs", "programfiles", "software", "system", "update", "updates",
        "setup", "installer", "install", "app", "apps", "application", "data", "temp", "cache", "user", "users", "default", "public",
        "inc", "corp", "corporation", "ltd", "llc", "gmbh", "company", "the", "for", "x64", "x86", "bit", "64bit", "32bit", "free", "pro",
        "google", "intel", "nvidia", "amd", "realtek", "adobe", "apple", "mozilla", "oracle", "classes", "policies", "wow6432node", "clients",
        "packages", "local", "roaming", "locallow", "tools", "driver", "drivers", "runtime", "redistributable", "visual", "net", "framework",
    };

    private static List<string> NameTokens(InstalledProgram p)
    {
        var tokens = new List<string>();
        var full = Util.Normalize(StripVersion(p.Name));
        if (full.Length >= 3) tokens.Add(full);
        foreach (var w in StripVersion(p.Name).Split([' ', '-', '_', '.', '(', ')', ','], StringSplitOptions.RemoveEmptyEntries))
        {
            var n = Util.Normalize(w);
            if (n.Length >= 4 && !GenericWords.Contains(n) && !n.All(char.IsDigit)) tokens.Add(n);
        }
        // Nombre de la carpeta de instalación
        if (!string.IsNullOrWhiteSpace(p.InstallLocation))
        {
            var folder = Util.Normalize(Path.GetFileName(p.InstallLocation.TrimEnd('\\')));
            if (folder.Length >= 4 && !GenericWords.Contains(folder)) tokens.Add(folder);
        }
        // Nombre del ejecutable del desinstalador / icono
        var exe = PathHelper.ExtractPath(p.DisplayIcon);
        if (exe != null)
        {
            var n = Util.Normalize(Path.GetFileNameWithoutExtension(exe));
            if (n.Length >= 4 && !GenericWords.Contains(n) && !n.StartsWith("unins") && !n.StartsWith("uninst") && n != "setup") tokens.Add(n);
        }
        return tokens.Distinct().ToList();
    }

    private static string StripVersion(string name)
    {
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => !(char.IsDigit(w[0]) || (w.StartsWith('v') && w.Length > 1 && char.IsDigit(w[1])) || w.StartsWith('(')));
        return string.Join(' ', words);
    }

    private static bool Matches(string candidate, List<string> tokens, ScanMode mode)
    {
        var n = Util.Normalize(candidate);
        if (n.Length < 3 || GenericWords.Contains(n)) return false;
        if (tokens.Count > 0 && n == tokens[0]) return true;
        foreach (var t in tokens)
        {
            if (n == t) return true;
            if (mode >= ScanMode.Moderado && t.Length >= 5 && n.Length >= 5 && (n.StartsWith(t) || t.StartsWith(n))) return true;
            if (mode == ScanMode.Avanzado && t.Length >= 6 && n.Contains(t)) return true;
        }
        return false;
    }

    public static List<Leftover> FindLeftovers(InstalledProgram p, ScanMode mode, CancellationToken ct)
    {
        var results = new List<Leftover>();
        var tokens = NameTokens(p);
        var publisher = Util.Normalize(p.Publisher.Split([',', '.'])[0]);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddDir(string dir, string reason)
        {
            if (!Directory.Exists(dir) || PathHelper.IsProtectedFolder(dir)) return;
            if (!seen.Add(dir)) return;
            // si una carpeta padre ya está incluida, no duplicar
            if (results.Any(r => !r.IsRegistry && dir.StartsWith(r.Path + "\\", StringComparison.OrdinalIgnoreCase))) return;
            results.RemoveAll(r => !r.IsRegistry && r.Path.StartsWith(dir + "\\", StringComparison.OrdinalIgnoreCase));
            results.Add(new Leftover { Path = dir, IsRegistry = false, IsDirectory = true, Size = Util.DirSize(dir), Reason = reason });
        }
        void AddFile(string file, string reason)
        {
            if (!File.Exists(file) || !seen.Add(file)) return;
            long size = 0; try { size = new FileInfo(file).Length; } catch { }
            results.Add(new Leftover { Path = file, IsRegistry = false, Size = size, Reason = reason });
        }
        void AddKey(RegistryHive hive, RegistryView view, string path, string? value, string reason)
        {
            var id = $"{hive}|{view}|{path}|{value}";
            if (!seen.Add(id)) return;
            results.Add(new Leftover { Path = path, IsRegistry = true, Hive = hive, View = view, ValueName = value, Reason = reason });
        }

        // ---- 1. Carpeta de instalación ----
        if (!string.IsNullOrWhiteSpace(p.InstallLocation))
            AddDir(Environment.ExpandEnvironmentVariables(p.InstallLocation).TrimEnd('\\'), "Carpeta de instalación");
        var uninstExe = PathHelper.ExtractPath(p.UninstallString);
        if (uninstExe != null && !p.IsMsi && Path.IsPathRooted(uninstExe))
        {
            var d = Path.GetDirectoryName(uninstExe);
            if (d != null && Matches(Path.GetFileName(d), tokens, mode)) AddDir(d, "Carpeta del desinstalador");
        }

        // ---- 2. Carpetas comunes ----
        var roots = new List<string>
        {
            Util.Env("ProgramFiles"), Util.Env("ProgramFiles(x86)"), Util.ProgramData,
            Util.LocalAppData, Util.RoamingAppData, Path.Combine(Util.UserProfile, @"AppData\LocalLow"),
            Path.Combine(Util.LocalAppData, "Programs"),
            Path.Combine(Util.Env("ProgramFiles"), "Common Files"), Path.Combine(Util.Env("ProgramFiles(x86)"), "Common Files"),
            Path.Combine(Util.UserProfile, "Documents"),
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
        };
        foreach (var root in roots.Where(r => !string.IsNullOrEmpty(r)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            foreach (var d in Util.SafeDirs(root))
            {
                if (Matches(d.Name, tokens, mode)) { AddDir(d.FullName, "Carpeta de datos del programa"); continue; }
                // carpeta del fabricante -> buscar dentro
                if (mode >= ScanMode.Moderado && publisher.Length >= 3 && Util.Normalize(d.Name) == publisher && !GenericWords.Contains(publisher))
                {
                    var subs = Util.SafeDirs(d.FullName).ToList();
                    foreach (var sd in subs)
                        if (Matches(sd.Name, tokens, mode)) AddDir(sd.FullName, "Carpeta dentro del fabricante");
                    // carpeta del fabricante vacía o solo con restos de este programa
                    if (mode == ScanMode.Avanzado && subs.Count > 0 && subs.All(sd => Matches(sd.Name, tokens, mode)))
                        AddDir(d.FullName, "Carpeta del fabricante (solo contiene este programa)");
                    else if (subs.Count == 0 && !Directory.EnumerateFiles(d.FullName).Any())
                        AddDir(d.FullName, "Carpeta del fabricante vacía");
                }
            }
        }

        // ---- 3. Accesos directos ----
        foreach (var dir in new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
            Path.Combine(Util.RoamingAppData, @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar"),
        })
        {
            foreach (var f in Util.SafeFiles(dir, "*.lnk", false))
                if (Matches(Path.GetFileNameWithoutExtension(f.Name), tokens, mode)) AddFile(f.FullName, "Acceso directo");
        }

        // ---- 4. Registro ----
        ct.ThrowIfCancellationRequested();
        if (StillInstalledKey(p)) AddKey(p.Hive, p.View, p.KeyPath, null, "Entrada de desinstalación");

        var regRoots = new List<(RegistryHive, RegistryView, string)>
        {
            (RegistryHive.CurrentUser, RegistryView.Registry64, "Software"),
            (RegistryHive.LocalMachine, RegistryView.Registry64, "SOFTWARE"),
        };
        if (Environment.Is64BitOperatingSystem) regRoots.Add((RegistryHive.LocalMachine, RegistryView.Registry32, "SOFTWARE"));

        foreach (var (hive, view, path) in regRoots)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var sw = baseKey.OpenSubKey(path);
                if (sw == null) continue;
                foreach (var sub in sw.GetSubKeyNames())
                {
                    if (Matches(sub, tokens, mode)) { AddKey(hive, view, $"{path}\\{sub}", null, "Clave de configuración"); continue; }
                    if (mode >= ScanMode.Moderado && publisher.Length >= 3 && Util.Normalize(sub) == publisher && !GenericWords.Contains(publisher))
                    {
                        using var pub = sw.OpenSubKey(sub);
                        if (pub == null) continue;
                        var names = pub.GetSubKeyNames();
                        foreach (var ps in names)
                            if (Matches(ps, tokens, mode)) AddKey(hive, view, $"{path}\\{sub}\\{ps}", null, "Clave dentro del fabricante");
                        if (names.Length == 0 && pub.ValueCount == 0) AddKey(hive, view, $"{path}\\{sub}", null, "Clave de fabricante vacía");
                    }
                }
            }
            catch { }
        }

        // ---- 5. Inicio automático, App Paths, Firewall (modo avanzado) ----
        var installDir = string.IsNullOrWhiteSpace(p.InstallLocation) ? null : Environment.ExpandEnvironmentVariables(p.InstallLocation).TrimEnd('\\');
        foreach (var (hive, view, path) in new[]
        {
            (RegistryHive.CurrentUser, RegistryView.Registry64, @"Software\Microsoft\Windows\CurrentVersion\Run"),
            (RegistryHive.LocalMachine, RegistryView.Registry64, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"),
            (RegistryHive.LocalMachine, RegistryView.Registry32, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"),
        })
        {
            try
            {
                using var k = RegistryKey.OpenBaseKey(hive, view).OpenSubKey(path);
                if (k == null) continue;
                foreach (var v in k.GetValueNames())
                {
                    var data = k.GetValue(v) as string ?? "";
                    if (Matches(v, tokens, mode) || (installDir != null && installDir.Length > 3 && data.Contains(installDir, StringComparison.OrdinalIgnoreCase)))
                        AddKey(hive, view, path, v, "Programa de inicio");
                }
            }
            catch { }
        }
        if (mode >= ScanMode.Moderado)
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    const string ap = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths";
                    using var k = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view).OpenSubKey(ap);
                    if (k == null) continue;
                    foreach (var sub in k.GetSubKeyNames())
                    {
                        using var sk = k.OpenSubKey(sub);
                        var data = sk?.GetValue("") as string ?? "";
                        if ((installDir != null && installDir.Length > 3 && data.Contains(installDir, StringComparison.OrdinalIgnoreCase)) ||
                            Matches(Path.GetFileNameWithoutExtension(sub), tokens, ScanMode.Seguro))
                            AddKey(RegistryHive.LocalMachine, view, $"{ap}\\{sub}", null, "Ruta de aplicación");
                    }
                }
                catch { }
            }
        }

        return results;
    }

    private static bool StillInstalledKey(InstalledProgram p) => StillInstalled(p);

    /// <summary>Restos para una desinstalación forzada indicada solo por nombre/carpeta.</summary>
    public static InstalledProgram FakeProgram(string name, string? folder) => new()
    {
        Name = name,
        InstallLocation = folder ?? "",
        Hive = RegistryHive.LocalMachine,
        View = RegistryView.Registry64,
        KeyName = "__forced__" + Guid.NewGuid().ToString("N"),
    };

    public static (int ok, int failed) DeleteLeftovers(IEnumerable<Leftover> items)
    {
        int ok = 0, fail = 0;
        var list = items.ToList();
        // backup de claves del registro
        var regItems = list.Where(i => i.IsRegistry).ToList();
        if (regItems.Count > 0) BackupLeftoverKeys(regItems);

        foreach (var i in list)
        {
            try
            {
                if (i.IsRegistry)
                {
                    using var baseKey = RegistryKey.OpenBaseKey(i.Hive, i.View);
                    if (i.ValueName != null)
                    {
                        using var k = baseKey.OpenSubKey(i.Path, true);
                        k?.DeleteValue(i.ValueName, false);
                    }
                    else
                    {
                        var parent = i.Path[..i.Path.LastIndexOf('\\')];
                        var name = i.Path[(i.Path.LastIndexOf('\\') + 1)..];
                        using var pk = baseKey.OpenSubKey(parent, true);
                        pk?.DeleteSubKeyTree(name, false);
                    }
                    ok++;
                }
                else if (i.IsDirectory)
                {
                    if (PathHelper.IsProtectedFolder(i.Path)) { fail++; continue; }
                    if (Util.TryDeleteDirectory(i.Path)) ok++; else fail++;
                }
                else
                {
                    if (Util.TryDeleteFile(i.Path)) ok++; else fail++;
                }
            }
            catch { fail++; }
        }
        return (ok, fail);
    }

    private static void BackupLeftoverKeys(List<Leftover> items)
    {
        var issues = items.Select(i => new RegistryIssue
        {
            Category = "Restos",
            Hive = i.Hive,
            View = i.View,
            KeyPath = i.Path,
            ValueName = i.ValueName,
            Problem = i.Reason,
        });
        RegistryCleaner.Backup(issues);
    }

    // ===================== Apps de Microsoft Store =====================

    public static List<StoreApp> GetStoreApps()
    {
        var (code, output) = Util.PowerShell(
            "Get-AppxPackage | Where-Object { -not $_.IsFramework -and $_.SignatureKind -ne 'System' -and -not $_.NonRemovable } | " +
            "Select-Object Name, PackageFullName, Publisher, Version, InstallLocation | ConvertTo-Json -Compress", 120_000);
        var list = new List<StoreApp>();
        if (code != 0 || string.IsNullOrWhiteSpace(output)) return list;
        try
        {
            var json = output.Trim();
            var start = json.IndexOfAny(['[', '{']);
            if (start < 0) return list;
            json = json[start..];
            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            if (json.StartsWith('[')) list = JsonSerializer.Deserialize<List<StoreApp>>(json, opts) ?? [];
            else { var one = JsonSerializer.Deserialize<StoreApp>(json, opts); if (one != null) list.Add(one); }
        }
        catch { }
        foreach (var a in list)
        {
            var pub = a.Publisher;
            var cn = pub.IndexOf("CN=", StringComparison.Ordinal);
            if (cn >= 0) { pub = pub[(cn + 3)..]; var comma = pub.IndexOf(','); if (comma > 0) pub = pub[..comma]; }
            a.Publisher = pub.Trim('"');
        }
        return list.OrderBy(a => a.Name).ToList();
    }

    public static bool RemoveStoreApp(StoreApp app)
    {
        var (code, _) = Util.PowerShell($"Remove-AppxPackage -Package '{app.PackageFullName.Replace("'", "")}' -ErrorAction Stop", 300_000);
        return code == 0;
    }
}
