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
    public string Website { get; init; } = "";
    public string HelpLink { get; init; } = "";
    public string UpdateLink { get; init; } = "";

    /// <summary>Identificador estable para comentarios y programas ocultos.</summary>
    public string Id => $"{Hive}|{View}|{KeyName}";
    public string Comment => AppSettings.Current.ProgramComments.GetValueOrDefault(Id, "");
    public bool IsRecent => InstallDate != null && InstallDate.Value >= DateTime.Today.AddDays(-7);
    /// <summary>Grupo de la lista, como en Revo: instalados en los últimos 7 días y el resto.</summary>
    public string Group => IsRecent ? Loc.T("Instalados recientemente") : Loc.T("Todos los programas");
    public string WebsiteText => Website.Replace("https://", "").Replace("http://", "").TrimEnd('/');

    public string KeyPath => $@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{KeyName}";
    public string SizeText => SizeBytes > 0 ? Util.FormatBytes(SizeBytes) : "";
    public string DateText => InstallDate?.ToString("dd/MM/yyyy") ?? "";
    public string Arch => View == RegistryView.Registry32 && Environment.Is64BitOperatingSystem ? Loc.T("32 bits") : Loc.T("64 bits");
    public string Scope => Hive == RegistryHive.CurrentUser ? Loc.T("Usuario actual") : Loc.T("Todos los usuarios");
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
    public string Reason { get; set; } = "";
    public bool Selected { get; set; } = true;
    /// <summary>Otro programa instalado podría usar este elemento: se muestra sin marcar.</summary>
    public bool Shared { get; set; }
    /// <summary>Elementos que no son archivos ni claves: "service", "task" o "firewall".</summary>
    public string? Special { get; init; }
    /// <summary>Nombre del servicio, ruta completa de la tarea o identificador de la regla del firewall.</summary>
    public string? Target { get; init; }
    /// <summary>Solo coincide por el nombre (no apunta a la carpeta del programa): se muestra sin marcar.</summary>
    public bool ByNameOnly { get; set; }

    public Leftover Clone() => (Leftover)MemberwiseClone();

    public string ReasonText => Loc.T(Reason)
        + (Shared ? " · " + Loc.T("posible uso por otro programa") : "")
        + (ByNameOnly ? " · " + Loc.T("coincide solo por el nombre") : "");
    public string Kind => Special switch
    {
        "service" => Loc.T("Servicio"),
        "task" => Loc.T("Tarea programada"),
        "firewall" => Loc.T("Regla del firewall"),
        _ => IsRegistry ? (ValueName != null ? Loc.T("Valor del registro") : Loc.T("Clave del registro")) : IsDirectory ? Loc.T("Carpeta") : Loc.T("Archivo"),
    };
    public string Display => Special != null ? Path : IsRegistry
        ? (Hive == RegistryHive.CurrentUser ? "HKCU\\" : "HKLM\\") + Path + (ValueName != null ? $"  →  {ValueName}" : "") + (View == RegistryView.Registry32 ? Loc.T("  (32 bits)") : "")
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
    public string DisplayName { get; set; } = "";
    public string PublisherDisplayName { get; set; } = "";
    public bool Selected { get; set; }
}

public static partial class UninstallerService
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
                            Website = (sk.GetValue("URLInfoAbout") as string ?? "").Trim(),
                            HelpLink = (sk.GetValue("HelpLink") as string ?? "").Trim(),
                            UpdateLink = (sk.GetValue("URLUpdateInfo") as string ?? "").Trim(),
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

    /// <summary>
    /// Busca el icono del programa. Muchos instaladores no lo indican en el registro, así que se prueban
    /// varias fuentes en orden: DisplayIcon → icono del instalador MSI → ejecutable principal de la carpeta
    /// de instalación → acceso directo del menú Inicio → ejecutable del desinstalador.
    /// </summary>
    public static void LoadIcon(InstalledProgram p)
    {
        try
        {
            p.Icon = Native.GetIcon(p.DisplayIcon)
                     ?? Native.GetIcon(MsiProductIcon(p))
                     ?? Native.GetIcon(MainExecutable(p))
                     ?? Native.GetIcon(StartMenuTarget(p))
                     ?? UninstallerIcon(p);
        }
        catch { }
    }

    private static ImageSource? UninstallerIcon(InstalledProgram p)
    {
        var exe = PathHelper.ExtractPath(p.UninstallString);
        return exe != null && !exe.Contains("msiexec", StringComparison.OrdinalIgnoreCase) ? Native.GetIcon(exe) : null;
    }

    /// <summary>Icono que Windows Installer guarda para los productos MSI (HKCR\Installer\Products\...\ProductIcon).</summary>
    private static string? MsiProductIcon(InstalledProgram p)
    {
        if (!Guid.TryParse(p.KeyName, out var guid)) return null;
        // Windows Installer guarda el GUID "empaquetado": cada bloque con los caracteres invertidos
        var g = guid.ToString("N").ToUpperInvariant();
        string Rev(string s) { var a = s.ToCharArray(); Array.Reverse(a); return new string(a); }
        var packed = Rev(g[..8]) + Rev(g[8..12]) + Rev(g[12..16]);
        for (int i = 16; i < 32; i += 2) packed += Rev(g.Substring(i, 2));
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        {
            try
            {
                using var k = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64).OpenSubKey($@"SOFTWARE\Classes\Installer\Products\{packed}");
                if (k?.GetValue("ProductIcon") is string icon && icon.Length > 0) return icon;
            }
            catch { }
        }
        return null;
    }

    /// <summary>Ejecutable principal: el .exe de la carpeta de instalación cuyo nombre se parece al del programa (o el más grande).</summary>
    public static string? MainExecutable(InstalledProgram p)
    {
        var dir = Environment.ExpandEnvironmentVariables(p.InstallLocation ?? "").TrimEnd('\\');
        if (dir.Length <= 3 || !Directory.Exists(dir)) return null;
        var name = Util.Normalize(StripVersion(p.Name));
        var exes = Util.SafeFiles(dir, "*.exe", false)
            .Concat(Util.SafeDirs(dir).Where(d => d.Name is "bin" or "Application" or "app").SelectMany(d => Util.SafeFiles(d.FullName, "*.exe", false)))
            .Where(f => !f.Name.StartsWith("unins", StringComparison.OrdinalIgnoreCase) && !f.Name.Contains("setup", StringComparison.OrdinalIgnoreCase)
                        && !f.Name.Contains("update", StringComparison.OrdinalIgnoreCase) && !f.Name.Contains("crash", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (exes.Count == 0) return null;
        var byName = exes.FirstOrDefault(f =>
        {
            var n = Util.Normalize(Path.GetFileNameWithoutExtension(f.Name));
            return n.Length >= 3 && (name.Contains(n) || n.Contains(name));
        });
        try { return (byName ?? exes.OrderByDescending(f => f.Length).First()).FullName; } catch { return null; }
    }

    // Accesos directos del menú Inicio (nombre normalizado -> archivo .lnk); se leen una sola vez
    private static readonly Lazy<List<(string name, string path)>> StartMenuShortcuts = new(() =>
    {
        var list = new List<(string, string)>();
        foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), Environment.GetFolderPath(Environment.SpecialFolder.Programs) })
            foreach (var f in Util.SafeFiles(root, "*.lnk"))
                list.Add((Util.Normalize(Path.GetFileNameWithoutExtension(f.Name)), f.FullName));
        return list;
    });

    private static string? StartMenuTarget(InstalledProgram p)
    {
        var name = Util.Normalize(StripVersion(p.Name));
        if (name.Length < 3) return null;
        var hit = StartMenuShortcuts.Value.FirstOrDefault(s => s.name == name);
        if (hit.path == null) hit = StartMenuShortcuts.Value.FirstOrDefault(s => s.name.Length >= 4 && (name.StartsWith(s.name) || s.name.StartsWith(name)));
        if (hit.path == null) return null;
        var target = ResolveShortcut(hit.path);
        return target != null && File.Exists(target) ? target : null;
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
        // Palabras de nombre de producto demasiado comunes: «Studio» juntaría OBS Studio, Android Studio y Visual Studio Code
        "studio", "live", "player", "editor", "manager", "launcher", "center", "centre", "desktop", "client", "helper", "service", "services",
        "cloud", "online", "plus", "lite", "suite", "toolkit", "code", "office", "studios", "games", "game", "media", "video", "audio",
        "music", "photo", "photos", "browser", "web", "connect", "sync", "security", "assistant", "creator", "maker", "converter",
        "download", "downloader", "viewer", "reader", "recorder", "capture", "screen", "remote", "portable", "edition", "home", "basic",
        "premium", "ultimate", "professional", "enterprise", "standard", "community", "express", "beta", "preview", "insider",
    };

    /// <summary>
    /// Coincidencia fuerte: el nombre completo del programa, su carpeta de instalación o su ejecutable.
    /// Todo lo demás (una palabra suelta, o el nombre dentro de otro más largo) es débil y se muestra sin marcar.
    /// </summary>
    private static bool StrongMatch(string candidate, InstalledProgram p)
    {
        var n = Util.Normalize(candidate);
        if (n.Length < 3) return false;
        var strong = new List<string> { Util.Normalize(StripVersion(p.Name)) };
        if (!string.IsNullOrWhiteSpace(p.InstallLocation)) strong.Add(Util.Normalize(Path.GetFileName(Environment.ExpandEnvironmentVariables(p.InstallLocation).TrimEnd('\\'))));
        var exe = PathHelper.ExtractPath(p.DisplayIcon);
        if (exe != null) strong.Add(Util.Normalize(Path.GetFileNameWithoutExtension(exe)));
        return strong.Any(t => t.Length >= 4 && (n == t || (t.Length >= 6 && n.Length >= 6 && (n.StartsWith(t) || t.StartsWith(n)))));
    }

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
            AddDir(Environment.ExpandEnvironmentVariables(p.InstallLocation).TrimEnd('\\'), Loc.T("Carpeta de instalación"));
        var uninstExe = PathHelper.ExtractPath(p.UninstallString);
        if (uninstExe != null && !p.IsMsi && Path.IsPathRooted(uninstExe))
        {
            var d = Path.GetDirectoryName(uninstExe);
            if (d != null && Matches(Path.GetFileName(d), tokens, mode)) AddDir(d, "Carpeta del desinstalador");
        }

        // ---- 2. Carpetas comunes ----
        var roots = new List<string>
        {
            Util.Env("ProgramFiles"), Util.Env(Loc.T("ProgramFiles(x86)")), Util.ProgramData,
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
                if (Matches(d.Name, tokens, mode)) { AddDir(d.FullName, Loc.T("Carpeta de datos del programa")); continue; }
                // carpeta del fabricante -> buscar dentro
                if (mode >= ScanMode.Moderado && publisher.Length >= 3 && Util.Normalize(d.Name) == publisher && !GenericWords.Contains(publisher))
                {
                    var subs = Util.SafeDirs(d.FullName).ToList();
                    foreach (var sd in subs)
                        if (Matches(sd.Name, tokens, mode)) AddDir(sd.FullName, Loc.T("Carpeta dentro del fabricante"));
                    // carpeta del fabricante vacía o solo con restos de este programa
                    if (mode == ScanMode.Avanzado && subs.Count > 0 && subs.All(sd => Matches(sd.Name, tokens, mode)))
                        AddDir(d.FullName, Loc.T("Carpeta del fabricante (solo contiene este programa)"));
                    else if (subs.Count == 0 && !Directory.EnumerateFiles(d.FullName).Any())
                        AddDir(d.FullName, Loc.T("Carpeta del fabricante vacía"));
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
        if (StillInstalledKey(p)) AddKey(p.Hive, p.View, p.KeyPath, null, Loc.T("Entrada de desinstalación"));

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
                    if (Matches(sub, tokens, mode)) { AddKey(hive, view, $"{path}\\{sub}", null, Loc.T("Clave de configuración")); continue; }
                    if (mode >= ScanMode.Moderado && publisher.Length >= 3 && Util.Normalize(sub) == publisher && !GenericWords.Contains(publisher))
                    {
                        using var pub = sw.OpenSubKey(sub);
                        if (pub == null) continue;
                        var names = pub.GetSubKeyNames();
                        foreach (var ps in names)
                            if (Matches(ps, tokens, mode)) AddKey(hive, view, $"{path}\\{sub}\\{ps}", null, Loc.T("Clave dentro del fabricante"));
                        if (names.Length == 0 && pub.ValueCount == 0) AddKey(hive, view, $"{path}\\{sub}", null, Loc.T("Clave de fabricante vacía"));
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
                        AddKey(hive, view, path, v, Loc.T("Programa de inicio"));
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
                        {
                            // App Paths es la misma clave en las vistas de 32 y 64 bits: listarla una sola vez
                            if (seen.Add("apppath|" + sub.ToLowerInvariant()))
                                AddKey(RegistryHive.LocalMachine, view, $"{ap}\\{sub}", null, Loc.T("Ruta de aplicación"));
                        }
                    }
                }
                catch { }
            }
        }

        // ---- 6. Servicios, tareas, firewall, menú contextual y asociaciones (modo moderado o avanzado) ----
        if (mode >= ScanMode.Moderado)
        {
            ct.ThrowIfCancellationRequested();
            try { results.AddRange(FindSystemLeftovers(p, tokens, mode, seen)); } catch { }
        }

        // ---- 7. Rastros que deja Windows al usar el programa: temporales, compatibilidad, nombres y Prefetch ----
        if (mode >= ScanMode.Moderado)
        {
            ct.ThrowIfCancellationRequested();
            try { FindUsageTraces(p, tokens, mode, AddDir, AddFile, AddKey); } catch { }
        }

        MarkShared(p, results);
        // Lo encontrado solo por una palabra o un nombre parecido se muestra sin marcar (el usuario decide)
        foreach (var r in results)
        {
            if (r.Special != null || r.ValueName != null || r.Reason == Loc.T("Carpeta de instalación") || r.Reason == Loc.T("Entrada de desinstalación")) continue;
            var leaf = Path.GetFileNameWithoutExtension(r.Path.TrimEnd('\\'));
            if (!StrongMatch(leaf, p)) { r.ByNameOnly = true; r.Selected = false; }
        }
        return results;
    }

    /// <summary>
    /// Seguridad: si el nombre de una carpeta o clave coincide con OTRO programa instalado
    /// (p. ej. «Python» al desinstalar «Python Launcher») o es su carpeta de instalación, se deja sin marcar.
    /// </summary>
    private static void MarkShared(InstalledProgram p, List<Leftover> results)
    {
        var fullName = Util.Normalize(StripVersion(p.Name));
        var others = GetInstalledPrograms(includeSystem: true)
            .Where(o => !(o.KeyName == p.KeyName && o.Hive == p.Hive && o.View == p.View) && o.Name != p.Name).ToList();
        var otherNames = others.Select(o => Util.Normalize(StripVersion(o.Name))).Where(n => n.Length >= 4 && n != fullName).Distinct().ToList();
        var otherDirs = others.Select(o => Environment.ExpandEnvironmentVariables(o.InstallLocation).TrimEnd('\\')).Where(d => d.Length > 3).ToList();
        foreach (var r in results)
        {
            if (r.ValueName != null || (r.IsRegistry && r.Path == p.KeyPath)) continue;
            var leaf = Util.Normalize(Path.GetFileNameWithoutExtension(r.Path.TrimEnd('\\')));
            bool shared = leaf.Length >= 4 && leaf != fullName && otherNames.Any(n => n.StartsWith(leaf) || leaf.StartsWith(n));
            if (!r.IsRegistry && r.IsDirectory)
                shared |= otherDirs.Any(d => d.Equals(r.Path, StringComparison.OrdinalIgnoreCase) || d.StartsWith(r.Path + "\\", StringComparison.OrdinalIgnoreCase));
            if (shared) { r.Shared = true; r.Selected = false; }
        }
    }

    private static bool StillInstalledKey(InstalledProgram p) => StillInstalled(p);

    /// <summary>Tipos de archivo y de enlace que usan muchos programas: nunca se marcan solos.</summary>
    private static readonly HashSet<string> SharedProgIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "mailto", "http", "https", "ftp", "tel", "callto", "sip", "sips", "im", "sms", "webcal", "webcals", "news", "nntp", "snews",
        "irc", "ircs", "magnet", "feed", "feeds", "ldap", "file", "htmlfile", "txtfile", "jpegfile", "pngfile", "giffile", "pdffile",
        "AcroExch.Document", "Word.Document", "Excel.Sheet", "mhtmlfile", "xmlfile", "zip", "CompressedFolder",
    };

    /// <summary>Carpetas que pertenecen con seguridad al programa (para saber si algo «apunta» a él).</summary>
    private static List<string> KnownDirs(InstalledProgram p, List<string> tokens)
    {
        var dirs = new List<string>();
        void Add(string? d)
        {
            if (string.IsNullOrWhiteSpace(d)) return;
            d = Environment.ExpandEnvironmentVariables(d.Trim().Trim('"')).TrimEnd('\\');
            if (d.Length > 3 && !PathHelper.IsProtectedFolder(d) && !dirs.Contains(d, StringComparer.OrdinalIgnoreCase)) dirs.Add(d);
        }
        Add(p.InstallLocation);
        var icon = PathHelper.ExtractPath(p.DisplayIcon);
        if (icon != null && Path.IsPathRooted(icon)) Add(Path.GetDirectoryName(icon));
        var uninst = PathHelper.ExtractPath(p.UninstallString);
        if (uninst != null && !p.IsMsi && Path.IsPathRooted(uninst))
        {
            var d = Path.GetDirectoryName(uninst);
            if (d != null && Matches(Path.GetFileName(d), tokens, ScanMode.Moderado)) Add(d);
        }
        return dirs;
    }

    internal static bool PointsTo(string? text, List<string> dirs)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var t = Environment.ExpandEnvironmentVariables(text);
        return dirs.Any(d => t.Contains(d + "\\", StringComparison.OrdinalIgnoreCase) || t.Contains(d + "\"", StringComparison.OrdinalIgnoreCase)
                             || t.TrimEnd('"').Equals(d, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Servicios, tareas programadas, reglas del firewall, menús contextuales y asociaciones de archivos.</summary>
    private static List<Leftover> FindSystemLeftovers(InstalledProgram p, List<string> tokens, ScanMode mode, HashSet<string> seen)
    {
        var found = new List<Leftover>();
        var dirs = KnownDirs(p, tokens);
        var winDir = Util.WinDir;

        // Servicios
        try
        {
            using var services = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey(@"SYSTEM\CurrentControlSet\Services");
            foreach (var name in services?.GetSubKeyNames() ?? [])
            {
                using var k = services!.OpenSubKey(name);
                var image = k?.GetValue("ImagePath") as string;
                var display = k?.GetValue("DisplayName") as string ?? "";
                if (string.IsNullOrWhiteSpace(image)) continue;
                bool strong = PointsTo(image, dirs);
                bool byName = !strong && (Matches(name, tokens, ScanMode.Seguro) || (!display.StartsWith('@') && Matches(display, tokens, ScanMode.Seguro)));
                if (!strong && !byName) continue;
                // Los controladores y servicios de Windows nunca, salvo que apunten a la carpeta del programa
                if (!strong && Environment.ExpandEnvironmentVariables(image).Contains(winDir, StringComparison.OrdinalIgnoreCase)) continue;
                if (!seen.Add("svc|" + name)) continue;
                found.Add(new Leftover
                {
                    Path = string.IsNullOrWhiteSpace(display) || display.StartsWith('@') || display.Contains(name, StringComparison.OrdinalIgnoreCase)
                        ? (string.IsNullOrWhiteSpace(display) || display.StartsWith('@') ? name : display)
                        : $"{display} ({name})",
                    IsRegistry = false, Special = "service", Target = name, Reason = "Servicio de Windows",
                    ByNameOnly = byName, Selected = strong,
                });
            }
        }
        catch { }

        // Tareas programadas (fuera de \Microsoft\)
        var (code, output) = Util.PowerShell(
            "Get-ScheduledTask | Where-Object { $_.TaskPath -notlike '\\Microsoft\\*' } | ForEach-Object { " +
            "$_.TaskPath + '|' + $_.TaskName + '|' + (($_.Actions | ForEach-Object { $_.Execute + ' ' + $_.Arguments }) -join ' ; ') }", 60_000);
        if (code == 0)
            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Trim().Split('|', 3);
                if (parts.Length < 3) continue;
                bool strong = PointsTo(parts[2], dirs);
                bool byName = !strong && Matches(parts[1], tokens, ScanMode.Seguro);
                if (!strong && !byName) continue;
                var full = parts[0] + parts[1];
                if (!seen.Add("task|" + full)) continue;
                found.Add(new Leftover
                {
                    Path = full, IsRegistry = false, Special = "task", Target = full, Reason = "Tarea programada",
                    ByNameOnly = byName, Selected = strong,
                });
            }

        // Reglas del firewall (registro: ...\FirewallPolicy\FirewallRules)
        try
        {
            const string fw = @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\FirewallRules";
            using var rules = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey(fw);
            foreach (var id in rules?.GetValueNames() ?? [])
            {
                if (rules!.GetValue(id) is not string data) continue;
                string Field(string key)
                {
                    var i = data.IndexOf("|" + key + "=", StringComparison.OrdinalIgnoreCase);
                    if (i < 0) return "";
                    var start = i + key.Length + 2;
                    var end = data.IndexOf('|', start);
                    return end < 0 ? data[start..] : data[start..end];
                }
                var app = Field("App");
                var ruleName = Field("Name");
                bool strong = PointsTo(app, dirs);
                bool byName = !strong && !ruleName.StartsWith('@') && Matches(ruleName, tokens, ScanMode.Seguro);
                if (!strong && !byName) continue;
                if (!seen.Add("fw|" + id)) continue;
                found.Add(new Leftover
                {
                    Path = $"{(ruleName.StartsWith('@') ? id : ruleName)} ({Field("Dir")})", IsRegistry = false, Special = "firewall", Target = id,
                    Hive = RegistryHive.LocalMachine, View = RegistryView.Registry64, ValueName = id,
                    Reason = "Regla del firewall", ByNameOnly = byName, Selected = strong,
                });
            }
        }
        catch { }

        // Menú contextual (clic derecho) y asociaciones de archivos
        string[] menuRoots =
        [
            @"*\shell", @"Directory\shell", @"Directory\Background\shell", @"Folder\shell", @"Drive\shell",
            @"*\shellex\ContextMenuHandlers", @"Directory\shellex\ContextMenuHandlers",
            @"Directory\Background\shellex\ContextMenuHandlers", @"Folder\shellex\ContextMenuHandlers",
        ];
        foreach (var (hive, classes) in new[] { (RegistryHive.LocalMachine, @"SOFTWARE\Classes"), (RegistryHive.CurrentUser, @"Software\Classes") })
        {
            RegistryKey? baseKey = null;
            try { baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64).OpenSubKey(classes); } catch { }
            if (baseKey == null) continue;
            using (baseKey)
            {
                foreach (var root in menuRoots)
                {
                    using var rk = baseKey.OpenSubKey(root);
                    foreach (var sub in rk?.GetSubKeyNames() ?? [])
                    {
                        string? target = null;
                        try
                        {
                            using var sk = rk!.OpenSubKey(sub);
                            if (root.EndsWith("shell")) target = sk?.OpenSubKey("command")?.GetValue("") as string;
                            else
                            {
                                // ContextMenuHandlers: el valor es un CLSID; su DLL está en CLSID\{...}\InprocServer32
                                var clsid = sk?.GetValue("") as string;
                                if (clsid == null && sub.StartsWith('{')) clsid = sub;
                                if (clsid != null)
                                {
                                    using var inproc = Registry.ClassesRoot.OpenSubKey($@"CLSID\{clsid}\InprocServer32");
                                    target = inproc?.GetValue("") as string;
                                }
                            }
                        }
                        catch { }
                        bool strong = PointsTo(target, dirs);
                        bool byName = !strong && mode == ScanMode.Avanzado && Matches(sub, tokens, ScanMode.Seguro);
                        if (!strong && !byName) continue;
                        var path = $@"{classes}\{root}\{sub}";
                        if (!seen.Add($"{hive}|{RegistryView.Registry64}|{path}|")) continue;
                        found.Add(new Leftover
                        {
                            Path = path, IsRegistry = true, Hive = hive, View = RegistryView.Registry64,
                            Reason = "Menú contextual", ByNameOnly = byName, Selected = strong,
                        });
                    }
                }

                // Asociaciones de archivos: tipos (ProgID) cuyo «abrir con» ejecuta el programa
                if (dirs.Count > 0)
                    foreach (var progId in baseKey.GetSubKeyNames())
                    {
                        if (progId.StartsWith('.') || progId.StartsWith('*') || progId is "CLSID" or "Interface" or "TypeLib" or "AppID"
                            or "Installer" or "Directory" or "Folder" or "Drive" or "Applications" or "SystemFileAssociations") continue;
                        string? cmd = null;
                        try
                        {
                            using var c = baseKey.OpenSubKey($@"{progId}\shell\open\command");
                            cmd = c?.GetValue("") as string;
                        }
                        catch { }
                        if (!PointsTo(cmd, dirs)) continue;
                        var path = $@"{classes}\{progId}";
                        if (!seen.Add($"{hive}|{RegistryView.Registry64}|{path}|")) continue;
                        // Tipos de enlace compartidos (correo, web, llamadas...): otro programa puede necesitarlos
                        bool sharedType = SharedProgIds.Contains(progId) || progId.StartsWith("ms-", StringComparison.OrdinalIgnoreCase);
                        found.Add(new Leftover
                        {
                            Path = path, IsRegistry = true, Hive = hive, View = RegistryView.Registry64, Reason = "Asociación de archivos",
                            Shared = sharedType, Selected = !sharedType,
                        });
                    }
            }
        }
        return found;
    }

    /// <summary>Restos para una desinstalación forzada indicada solo por nombre/carpeta.</summary>
    public static InstalledProgram FakeProgram(string name, string? folder) => new()
    {
        Name = name,
        InstallLocation = folder ?? "",
        Hive = RegistryHive.LocalMachine,
        View = RegistryView.Registry64,
        KeyName = "__forced__" + Guid.NewGuid().ToString("N"),
    };

    /// <summary>Guarda una copia .reg de las claves que se van a borrar (llamar antes de DeleteLeftover).</summary>
    public static void BackupRegistryLeftovers(IEnumerable<Leftover> items)
    {
        var list = items.ToList();
        // Servicios y reglas del firewall también se guardan en el .reg
        var regItems = list.Where(i => i.IsRegistry).ToList();
        regItems.AddRange(list.Where(i => i.Special == "service").Select(i => new Leftover
        {
            Path = $@"SYSTEM\CurrentControlSet\Services\{i.Target}", IsRegistry = true, Hive = RegistryHive.LocalMachine, View = RegistryView.Registry64,
        }));
        regItems.AddRange(list.Where(i => i.Special == "firewall").Select(i => new Leftover
        {
            Path = @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\FirewallRules", IsRegistry = true,
            Hive = RegistryHive.LocalMachine, View = RegistryView.Registry64, ValueName = i.Target,
        }));
        if (regItems.Count > 0) BackupLeftoverKeys(regItems);

        // Las tareas programadas se exportan en XML (se pueden volver a importar desde el Programador de tareas)
        var tasks = list.Where(i => i.Special == "task").ToList();
        if (tasks.Count > 0)
        {
            var dir = Path.Combine(AppSettings.BackupFolder, $"Tareas_{DateTime.Now:yyyyMMdd_HHmmss}");
            Directory.CreateDirectory(dir);
            foreach (var t in tasks)
            {
                var slash = t.Target!.LastIndexOf('\\');
                var file = Path.Combine(dir, string.Concat(t.Target[(slash + 1)..].Split(Path.GetInvalidFileNameChars())) + ".xml");
                Util.PowerShell($"Export-ScheduledTask -TaskPath '{t.Target[..(slash + 1)].Replace("'", "''")}' -TaskName '{t.Target[(slash + 1)..].Replace("'", "''")}' " +
                                $"| Out-File -Encoding unicode '{file.Replace("'", "''")}'");
            }
        }
    }

    /// <summary>Elimina un resto (archivo, carpeta, clave o valor del registro). Devuelve true si se eliminó.</summary>
    public static bool DeleteLeftover(Leftover i)
    {
        try
        {
            switch (i.Special)
            {
                case "service":
                    Util.Run("sc.exe", $"stop \"{i.Target}\"", 30_000);
                    Util.Run("sc.exe", $"delete \"{i.Target}\"", 30_000);
                    using (var k = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{i.Target}"))
                        return k == null || (k.GetValue("DeleteFlag") is int flag && flag == 1);   // DeleteFlag: se elimina al reiniciar
                case "task":
                    var slash = i.Target!.LastIndexOf('\\');
                    var (taskCode, _) = Util.PowerShell($"Unregister-ScheduledTask -TaskPath '{i.Target[..(slash + 1)].Replace("'", "''")}' " +
                        $"-TaskName '{i.Target[(slash + 1)..].Replace("'", "''")}' -Confirm:$false -ErrorAction Stop");
                    return taskCode == 0;
                case "firewall":
                    var (fwCode, _) = Util.PowerShell($"Remove-NetFirewallRule -Name '{i.Target!.Replace("'", "''")}' -ErrorAction Stop");
                    return fwCode == 0;
            }
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
                return true;
            }
            // Archivos y carpetas a la Papelera de reciclaje (se pueden recuperar); si no se puede, se borran
            if (i.IsDirectory)
            {
                if (PathHelper.IsProtectedFolder(i.Path)) return false;
                return (Native.SendToRecycleBin(i.Path) && !Directory.Exists(i.Path)) || Util.TryDeleteDirectory(i.Path);
            }
            return (Native.SendToRecycleBin(i.Path) && !File.Exists(i.Path)) || Util.TryDeleteFile(i.Path);
        }
        catch { return false; }
    }

    public static (int ok, int failed) DeleteLeftovers(IEnumerable<Leftover> items)
    {
        var list = items.ToList();
        BackupRegistryLeftovers(list);
        int ok = list.Count(DeleteLeftover);
        return (ok, list.Count - ok);
    }

    private static void BackupLeftoverKeys(List<Leftover> items)
    {
        var issues = items.Select(i => new RegistryIssue
        {
            Category = Loc.T("Restos"),
            Hive = i.Hive,
            View = i.View,
            KeyPath = i.Path,
            ValueName = i.ValueName,
            Problem = i.Reason,
        });
        RegistryCleaner.Backup(issues);
    }

    // ===================== Modo cazador =====================

    /// <summary>Busca a qué programa instalado pertenece un ejecutable o acceso directo.</summary>
    public static InstalledProgram? FindOwner(string path, IEnumerable<InstalledProgram> programs)
    {
        if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) path = ResolveShortcut(path) ?? path;
        if (string.IsNullOrWhiteSpace(path)) return null;
        var dir = Path.GetDirectoryName(path) ?? "";
        InstalledProgram? best = null;
        int bestLen = 0;
        foreach (var p in programs)
        {
            foreach (var candidate in new[]
                     {
                         p.InstallLocation,
                         Path.GetDirectoryName(PathHelper.ExtractPath(p.DisplayIcon) ?? "") ?? "",
                         p.IsMsi ? "" : Path.GetDirectoryName(PathHelper.ExtractPath(p.UninstallString) ?? "") ?? "",
                     })
            {
                var c = Environment.ExpandEnvironmentVariables(candidate ?? "").TrimEnd('\\');
                if (c.Length <= 3 || PathHelper.IsProtectedFolder(c)) continue;
                if ((dir.Equals(c, StringComparison.OrdinalIgnoreCase) || dir.StartsWith(c + "\\", StringComparison.OrdinalIgnoreCase)) && c.Length > bestLen)
                {
                    best = p; bestLen = c.Length;
                }
            }
        }
        if (best != null) return best;
        // Último recurso: el nombre del producto del ejecutable
        try
        {
            var info = System.Diagnostics.FileVersionInfo.GetVersionInfo(path);
            var product = Util.Normalize(info.ProductName ?? "");
            if (product.Length >= 4)
                return programs.FirstOrDefault(p => Util.Normalize(p.Name).StartsWith(product) || product.StartsWith(Util.Normalize(p.Name)));
        }
        catch { }
        return null;
    }

    public static string? ResolveShortcut(string lnk)
    {
        try
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type == null) return null;
            dynamic shell = Activator.CreateInstance(type)!;
            dynamic sc = shell.CreateShortcut(lnk);
            string target = sc.TargetPath;
            return string.IsNullOrWhiteSpace(target) ? null : target;
        }
        catch { return null; }
    }

    // ===================== Apps de Microsoft Store =====================

    public static List<StoreApp> GetStoreApps()
    {
        var (code, output) = Util.PowerShell(
            "Get-AppxPackage | Where-Object { -not $_.IsFramework -and $_.SignatureKind -ne 'System' -and -not $_.NonRemovable } | " +
            "ForEach-Object { $m = $null; try { $m = (Get-AppxPackageManifest $_ -ErrorAction Stop).Package.Properties } catch {} ; " +
            "[pscustomobject]@{ Name=$_.Name; PackageFullName=$_.PackageFullName; Publisher=$_.Publisher; Version=[string]$_.Version; " +
            "InstallLocation=$_.InstallLocation; DisplayName=[string]$m.DisplayName; PublisherDisplayName=[string]$m.PublisherDisplayName } } | " +
            "ConvertTo-Json -Compress", 180_000);
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
            // Fabricante: el nombre del manifiesto; si no hay, el «CN=» del certificado (a veces es un código)
            var pub = a.PublisherDisplayName;
            if (string.IsNullOrWhiteSpace(pub) || pub.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase))
            {
                pub = a.Publisher;
                var cn = pub.IndexOf("CN=", StringComparison.Ordinal);
                if (cn >= 0) { pub = pub[(cn + 3)..]; var comma = pub.IndexOf(','); if (comma > 0) pub = pub[..comma]; }
            }
            a.Publisher = pub.Trim('"');
            // Nombre: el del manifiesto; si es un recurso sin resolver, el nombre técnico sin el prefijo («4DF9E0F8.Netflix» → «Netflix»)
            var name = a.DisplayName;
            if (string.IsNullOrWhiteSpace(name) || name.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase))
            {
                name = a.Name;
                var dot = name.IndexOf('.');
                if (dot > 0 && dot < name.Length - 1 && (name[..dot].Any(char.IsDigit) || name[..dot].Length >= 8)) name = name[(dot + 1)..];
            }
            a.Name = name;
        }
        return list.OrderBy(a => a.Name).ToList();
    }

    public static bool RemoveStoreApp(StoreApp app)
    {
        var (code, _) = Util.PowerShell($"Remove-AppxPackage -Package '{app.PackageFullName.Replace("'", "")}' -ErrorAction Stop", 300_000);
        return code == 0;
    }
}
