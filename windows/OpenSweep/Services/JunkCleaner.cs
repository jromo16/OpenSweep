using OpenSweep.Core;
using Microsoft.Win32;

namespace OpenSweep.Services;

/// <summary>Especifica una carpeta y un patrón de archivos a limpiar.</summary>
public sealed record PathSpec(string Dir, string Pattern = "*", bool Recursive = true, int MinAgeHours = 0, bool RemoveEmptyDirs = true);

public sealed class CleanRule
{
    public required string Id { get; init; }
    public required string Group { get; init; }     // Windows, Navegadores, Aplicaciones, Privacidad
    public required string Section { get; init; }   // p.ej. "Google Chrome"
    public required string Name { get; init; }
    public string? Warning { get; init; }
    public bool DefaultOn { get; init; } = true;
    public string[] Processes { get; init; } = [];  // procesos que deben estar cerrados
    public Func<IEnumerable<PathSpec>>? Paths { get; init; }
    public Func<(long bytes, long count)>? CustomMeasure { get; init; }
    public Action? CustomClean { get; init; }

    public bool Selected { get; set; }

    // Textos traducidos solo para mostrar (Group/Section/Name se usan también en la lógica)
    public string DisplayName => Loc.T(Name);
    public string DisplaySection => Loc.T(Section);
    public string? DisplayWarning => Warning == null ? null : Loc.T(Warning);
}

public sealed class RuleResult
{
    public required CleanRule Rule { get; init; }
    public long Bytes { get; set; }
    public long Count { get; set; }
    public long Failed { get; set; }
    public List<string> Files { get; } = [];
    public string Display => $"{Rule.DisplaySection} - {Rule.DisplayName}";
    public string SizeText => Util.FormatBytes(Bytes);
}

public static class JunkCleaner
{
    private static string L => Util.LocalAppData;
    private static string R => Util.RoamingAppData;

    // ---------------- Navegadores basados en Chromium ----------------
    private sealed record ChromiumBrowser(string Name, string UserData, string[] Processes);

    private static IEnumerable<ChromiumBrowser> ChromiumBrowsers() =>
    [
        new("Google Chrome", Path.Combine(L, @"Google\Chrome\User Data"), ["chrome"]),
        new("Microsoft Edge", Path.Combine(L, @"Microsoft\Edge\User Data"), ["msedge"]),
        new("Brave", Path.Combine(L, @"BraveSoftware\Brave-Browser\User Data"), ["brave"]),
        new("Vivaldi", Path.Combine(L, @"Vivaldi\User Data"), ["vivaldi"]),
        new("Opera", Path.Combine(R, @"Opera Software\Opera Stable"), ["opera"]),
        new("Opera GX", Path.Combine(R, @"Opera Software\Opera GX Stable"), ["opera"]),
        new("Chromium", Path.Combine(L, @"Chromium\User Data"), ["chromium"]),
    ];

    /// <summary>Devuelve las carpetas de perfil (Default, Profile 1, ...).</summary>
    public static IEnumerable<string> ChromiumProfiles(string userData)
    {
        if (!Directory.Exists(userData)) yield break;
        // Opera guarda el perfil directamente en la carpeta raíz
        if (File.Exists(Path.Combine(userData, "Preferences")) || File.Exists(Path.Combine(userData, "History")))
            yield return userData;
        foreach (var d in Util.SafeDirs(userData))
        {
            if (d.Name == "Default" || d.Name.StartsWith("Profile ") || d.Name.StartsWith("Guest Profile"))
                yield return d.FullName;
        }
    }

    private static IEnumerable<string> FirefoxProfiles(bool local)
    {
        var root = Path.Combine(local ? L : R, @"Mozilla\Firefox\Profiles");
        return Util.SafeDirs(root).Select(d => d.FullName);
    }

    private static PathSpec File1(string path) =>
        new(Path.GetDirectoryName(path)!, Path.GetFileName(path), false, 0, false);

    public static List<CleanRule> BuildRules()
    {
        var s = AppSettings.Current;
        var rules = new List<CleanRule>();
        var win = Util.WinDir;
        var pd = Util.ProgramData;

        // ================= WINDOWS =================
        rules.Add(new CleanRule
        {
            Id = "win.usertemp", Group = "Windows", Section = "Sistema", Name = "Archivos temporales del usuario",
            Paths = () => [new PathSpec(Path.GetTempPath(), "*", true, s.TempMinAgeHours)]
        });
        rules.Add(new CleanRule
        {
            Id = "win.systemp", Group = "Windows", Section = "Sistema", Name = "Archivos temporales de Windows",
            Paths = () => [new PathSpec(Path.Combine(win, "Temp"), "*", true, s.TempMinAgeHours)]
        });
        rules.Add(new CleanRule
        {
            Id = "win.recycle", Group = "Windows", Section = "Sistema", Name = "Papelera de reciclaje",
            CustomMeasure = () => Native.QueryRecycleBin(),
            CustomClean = () => Native.EmptyRecycleBin()
        });
        rules.Add(new CleanRule
        {
            Id = "win.thumbs", Group = "Windows", Section = "Explorador de Windows", Name = "Caché de miniaturas",
            Paths = () => [new PathSpec(Path.Combine(L, @"Microsoft\Windows\Explorer"), "thumbcache_*.db", false, 0, false),
                           new PathSpec(Path.Combine(L, @"Microsoft\Windows\Explorer"), "iconcache_*.db", false, 0, false)]
        });
        rules.Add(new CleanRule
        {
            Id = "win.wer", Group = "Windows", Section = "Sistema", Name = "Informes de errores de Windows",
            Paths = () => [new PathSpec(Path.Combine(pd, @"Microsoft\Windows\WER\ReportArchive")),
                           new PathSpec(Path.Combine(pd, @"Microsoft\Windows\WER\ReportQueue")),
                           new PathSpec(Path.Combine(pd, @"Microsoft\Windows\WER\Temp")),
                           new PathSpec(Path.Combine(L, @"Microsoft\Windows\WER")),
                           new PathSpec(Path.Combine(L, "CrashDumps"))]
        });
        rules.Add(new CleanRule
        {
            Id = "win.dumps", Group = "Windows", Section = "Sistema", Name = "Volcados de memoria",
            Paths = () => [new PathSpec(Path.Combine(win, "Minidump")), File1(Path.Combine(win, "MEMORY.DMP")),
                           new PathSpec(Path.Combine(win, "LiveKernelReports"), "*.dmp")]
        });
        rules.Add(new CleanRule
        {
            Id = "win.logs", Group = "Windows", Section = "Sistema", Name = "Archivos de registro (logs) de Windows",
            Paths = () => [new PathSpec(Path.Combine(win, "Logs"), "*.log", true, 72, false),
                           new PathSpec(Path.Combine(win, "Logs"), "*.etl", true, 72, false),
                           new PathSpec(Path.Combine(win, "Panther"), "*.log", true, 72, false),
                           new PathSpec(win, "*.log", false, 72, false),
                           new PathSpec(Path.Combine(win, @"System32\LogFiles"), "*.log", true, 72, false)]
        });
        rules.Add(new CleanRule
        {
            Id = "win.update", Group = "Windows", Section = "Sistema", Name = "Caché de Windows Update",
            Paths = () => [new PathSpec(Path.Combine(win, @"SoftwareDistribution\Download"), "*", true, 0, true)]
        });
        rules.Add(new CleanRule
        {
            Id = "win.delivery", Group = "Windows", Section = "Sistema", Name = "Caché de Optimización de distribución",
            Paths = () => [new PathSpec(Path.Combine(win, @"ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache"))]
        });
        rules.Add(new CleanRule
        {
            Id = "win.prefetch", Group = "Windows", Section = "Sistema", Name = "Datos de prefetch antiguos", DefaultOn = false,
            Paths = () => [new PathSpec(Path.Combine(win, "Prefetch"), "*.pf", false, 24 * 14, false)]
        });
        rules.Add(new CleanRule
        {
            Id = "win.shader", Group = "Windows", Section = "Sistema", Name = "Caché de sombreadores DirectX",
            Paths = () => [new PathSpec(Path.Combine(L, "D3DSCache")),
                           new PathSpec(Path.Combine(L, @"NVIDIA\DXCache")),
                           new PathSpec(Path.Combine(L, @"NVIDIA\GLCache")),
                           new PathSpec(Path.Combine(L, @"AMD\DxCache")),
                           new PathSpec(Path.Combine(L, @"AMD\DxcCache"))]
        });
        rules.Add(new CleanRule
        {
            Id = "win.installercache", Group = "Windows", Section = "Sistema", Name = "Archivos de instalación descargados (NVIDIA/AMD/Intel)", DefaultOn = false,
            Paths = () => [new PathSpec(Path.Combine(pd, @"NVIDIA Corporation\Downloader")),
                           new PathSpec(@"C:\AMD"),
                           new PathSpec(Path.Combine(pd, @"Intel\Package Cache"), "*", true, 24 * 30)]
        });
        rules.Add(new CleanRule
        {
            Id = "win.dns", Group = "Windows", Section = "Red", Name = "Caché DNS",
            CustomMeasure = () => (0, 1),
            CustomClean = () => Util.Run("ipconfig", "/flushdns")
        });
        rules.Add(new CleanRule
        {
            Id = "win.fonts", Group = "Windows", Section = "Sistema", Name = "Caché de fuentes", DefaultOn = false,
            Paths = () => [new PathSpec(Path.Combine(win, @"ServiceProfiles\LocalService\AppData\Local\FontCache"), "*.dat", true, 0, false)]
        });

        // ================= PRIVACIDAD =================
        rules.Add(new CleanRule
        {
            Id = "priv.recent", Group = "Privacidad", Section = "Explorador de Windows", Name = "Documentos recientes",
            Paths = () => [new PathSpec(Environment.GetFolderPath(Environment.SpecialFolder.Recent), "*.lnk", false, 0, false)]
        });
        rules.Add(new CleanRule
        {
            Id = "priv.jumplists", Group = "Privacidad", Section = "Explorador de Windows", Name = "Listas de accesos directos (Jump Lists)", DefaultOn = false,
            Paths = () => [new PathSpec(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Recent), "AutomaticDestinations")),
                           new PathSpec(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Recent), "CustomDestinations"))]
        });
        rules.Add(new CleanRule
        {
            Id = "priv.runmru", Group = "Privacidad", Section = "Explorador de Windows", Name = "Historial de Ejecutar y rutas escritas",
            CustomMeasure = () => (0, CountRegistryValues(@"Software\Microsoft\Windows\CurrentVersion\Explorer\RunMRU") +
                                      CountRegistryValues(@"Software\Microsoft\Windows\CurrentVersion\Explorer\TypedPaths")),
            CustomClean = () =>
            {
                ClearRegistryValues(@"Software\Microsoft\Windows\CurrentVersion\Explorer\RunMRU");
                ClearRegistryValues(@"Software\Microsoft\Windows\CurrentVersion\Explorer\TypedPaths");
                ClearRegistryValues(@"Software\Microsoft\Windows\CurrentVersion\Explorer\WordWheelQuery");
            }
        });
        rules.Add(new CleanRule
        {
            Id = "priv.clipboard", Group = "Privacidad", Section = "Sistema", Name = "Portapapeles",
            CustomMeasure = () => (0, 1),
            CustomClean = () =>
            {
                var t = new Thread(Native.ClearClipboard);
                t.SetApartmentState(ApartmentState.STA);
                t.Start(); t.Join();
            }
        });

        // ================= NAVEGADORES =================
        foreach (var b in ChromiumBrowsers())
        {
            if (!Directory.Exists(b.UserData)) continue;
            var ud = b.UserData;
            rules.Add(new CleanRule
            {
                Id = $"br.{b.Name}.cache", Group = "Navegadores", Section = b.Name, Name = "Caché de Internet", Processes = b.Processes,
                Paths = () => ChromiumProfiles(ud).SelectMany(p => new[]
                {
                    new PathSpec(Path.Combine(p, @"Cache")),
                    new PathSpec(Path.Combine(p, @"Code Cache")),
                    new PathSpec(Path.Combine(p, @"GPUCache")),
                    new PathSpec(Path.Combine(p, @"Service Worker\CacheStorage")),
                    new PathSpec(Path.Combine(p, @"Service Worker\ScriptCache")),
                    new PathSpec(Path.Combine(p, @"DawnGraphiteCache")),
                    new PathSpec(Path.Combine(p, @"DawnWebGPUCache")),
                }).Concat([new PathSpec(Path.Combine(ud, "ShaderCache")), new PathSpec(Path.Combine(ud, "GrShaderCache")),
                           new PathSpec(Path.Combine(ud, "GraphiteDawnCache")),
                           // Opera guarda la caché en Local
                           new PathSpec(ud.Replace(R, L, StringComparison.OrdinalIgnoreCase) is var lo && lo != ud ? Path.Combine(lo, "Cache") : "")])
            });
            rules.Add(new CleanRule
            {
                Id = $"br.{b.Name}.history", Group = "Navegadores", Section = b.Name, Name = "Historial de navegación", DefaultOn = true, Processes = b.Processes,
                Paths = () => ChromiumProfiles(ud).SelectMany(p => new[]
                {
                    File1(Path.Combine(p, "History")), File1(Path.Combine(p, "History-journal")),
                    File1(Path.Combine(p, "Visited Links")), File1(Path.Combine(p, "Top Sites")), File1(Path.Combine(p, "Top Sites-journal")),
                    File1(Path.Combine(p, "Shortcuts")), File1(Path.Combine(p, "Shortcuts-journal")),
                })
            });
            rules.Add(CookieRule($"br.{b.Name}.cookies", b.Name, b.Processes,
                () => ChromiumProfiles(ud).SelectMany(p => new[] { Path.Combine(p, @"Network\Cookies"), Path.Combine(p, "Cookies") }),
                () => ChromiumProfiles(ud).SelectMany(p => new[]
                {
                    File1(Path.Combine(p, @"Network\Cookies")), File1(Path.Combine(p, @"Network\Cookies-journal")),
                    File1(Path.Combine(p, "Cookies")), File1(Path.Combine(p, "Cookies-journal")),
                })));
            rules.Add(new CleanRule
            {
                Id = $"br.{b.Name}.session", Group = "Navegadores", Section = b.Name, Name = "Sesión (pestañas abiertas)", DefaultOn = false, Processes = b.Processes,
                Paths = () => ChromiumProfiles(ud).Select(p => new PathSpec(Path.Combine(p, "Sessions")))
            });
            rules.Add(new CleanRule
            {
                Id = $"br.{b.Name}.forms", Group = "Navegadores", Section = b.Name, Name = "Datos de autocompletar formularios", DefaultOn = false, Processes = b.Processes,
                Paths = () => ChromiumProfiles(ud).SelectMany(p => new[] { File1(Path.Combine(p, "Web Data")), File1(Path.Combine(p, "Web Data-journal")) })
            });
        }

        // Firefox
        if (Directory.Exists(Path.Combine(R, @"Mozilla\Firefox\Profiles")))
        {
            string[] ff = ["firefox"];
            rules.Add(new CleanRule
            {
                Id = "br.firefox.cache", Group = "Navegadores", Section = "Mozilla Firefox", Name = "Caché de Internet", Processes = ff,
                Paths = () => FirefoxProfiles(true).SelectMany(p => new[] { new PathSpec(Path.Combine(p, "cache2")), new PathSpec(Path.Combine(p, "startupCache")), new PathSpec(Path.Combine(p, "thumbnails")) })
                    .Concat(FirefoxProfiles(false).Select(p => new PathSpec(Path.Combine(p, "shader-cache"))))
            });
            rules.Add(CookieRule("br.firefox.cookies", "Mozilla Firefox", ff,
                () => FirefoxProfiles(false).Select(p => Path.Combine(p, "cookies.sqlite")),
                () => FirefoxProfiles(false).SelectMany(p => new[] { File1(Path.Combine(p, "cookies.sqlite")), File1(Path.Combine(p, "cookies.sqlite-wal")) })));
            rules.Add(new CleanRule
            {
                Id = "br.firefox.session", Group = "Navegadores", Section = "Mozilla Firefox", Name = "Sesión", DefaultOn = false, Processes = ff,
                Paths = () => FirefoxProfiles(false).SelectMany(p => new[] { File1(Path.Combine(p, "sessionstore.jsonlz4")), new PathSpec(Path.Combine(p, "sessionstore-backups")) })
            });
            rules.Add(new CleanRule
            {
                Id = "br.firefox.forms", Group = "Navegadores", Section = "Mozilla Firefox", Name = "Historial de formularios", DefaultOn = false, Processes = ff,
                Paths = () => FirefoxProfiles(false).Select(p => File1(Path.Combine(p, "formhistory.sqlite")))
            });
        }

        // ================= APLICACIONES =================
        void App(string id, string section, string name, string[] procs, params string[] dirs)
        {
            if (!dirs.Any(Directory.Exists)) return;
            rules.Add(new CleanRule
            {
                Id = "app." + id, Group = "Aplicaciones", Section = section, Name = name, Processes = procs,
                Paths = () => dirs.Select(d => new PathSpec(d))
            });
        }
        App("discord", "Discord", "Caché", ["Discord"], Path.Combine(R, @"discord\Cache"), Path.Combine(R, @"discord\Code Cache"), Path.Combine(R, @"discord\GPUCache"));
        App("spotify", "Spotify", "Caché", ["Spotify"], Path.Combine(L, @"Spotify\Data"), Path.Combine(L, @"Spotify\Browser\Cache"));
        App("teams", "Microsoft Teams", "Caché", ["Teams", "ms-teams"], Path.Combine(R, @"Microsoft\Teams\Cache"), Path.Combine(R, @"Microsoft\Teams\Code Cache"), Path.Combine(R, @"Microsoft\Teams\GPUCache"),
            Path.Combine(L, @"Packages\MSTeams_8wekyb3d8bbwe\LocalCache\Microsoft\MSTeams\EBWebView\Default\Cache"));
        App("vscode", "Visual Studio Code", "Caché", ["Code"], Path.Combine(R, @"Code\Cache"), Path.Combine(R, @"Code\CachedData"), Path.Combine(R, @"Code\Code Cache"), Path.Combine(R, @"Code\GPUCache"), Path.Combine(R, @"Code\logs"));
        App("steam", "Steam", "Caché web", ["steam", "steamwebhelper"], Path.Combine(L, @"Steam\htmlcache"));
        App("zoom", "Zoom", "Registros", ["Zoom"], Path.Combine(R, @"Zoom\logs"));
        App("slack", "Slack", "Caché", ["slack"], Path.Combine(R, @"Slack\Cache"), Path.Combine(R, @"Slack\Code Cache"), Path.Combine(R, @"Slack\GPUCache"));
        App("whatsapp", "WhatsApp", "Caché", ["WhatsApp"], Path.Combine(L, @"Packages\5319275A.WhatsAppDesktop_cv1g1gvanyjgm\LocalState\EBWebView\Default\Cache"));
        App("npm", "Desarrollo", "Caché de npm", [], Path.Combine(L, "npm-cache"));
        App("pip", "Desarrollo", "Caché de pip", [], Path.Combine(L, @"pip\cache"));
        App("nuget", "Desarrollo", "Caché HTTP de NuGet", [], Path.Combine(L, @"NuGet\v3-cache"));
        App("java", "Java", "Caché de Java", [], Path.Combine(L, @"Sun\Java\Deployment\cache"), Path.Combine(R, @"Sun\Java\Deployment\cache"));
        App("adobe", "Adobe", "Caché de medios", [], Path.Combine(R, @"Adobe\Common\Media Cache Files"), Path.Combine(R, @"Adobe\Common\Media Cache"));
        App("office", "Microsoft Office", "Caché de documentos", ["WINWORD", "EXCEL", "POWERPNT"], Path.Combine(L, @"Microsoft\Office\16.0\OfficeFileCache"));
        App("epic", "Epic Games", "Caché web", ["EpicGamesLauncher"], Path.Combine(L, @"EpicGamesLauncher\Saved\webcache"), Path.Combine(L, @"EpicGamesLauncher\Saved\webcache_4430"));

        // Carpetas personalizadas del usuario
        foreach (var folder in s.CustomFolders.Where(Directory.Exists))
        {
            var f = folder;
            rules.Add(new CleanRule
            {
                Id = "custom." + f, Group = "Personalizado", Section = "Carpetas incluidas", Name = f,
                Paths = () => [new PathSpec(f)]
            });
        }

        foreach (var r in rules)
            r.Selected = s.CleanerSelection.TryGetValue(r.Id, out var sel) ? sel : r.DefaultOn;
        return rules;
    }

    // ----------------- Cookies con lista blanca -----------------
    private static CleanRule CookieRule(string id, string section, string[] procs, Func<IEnumerable<string>> dbs, Func<IEnumerable<PathSpec>> files)
    {
        var keep = AppSettings.Current.CookieWhitelist;
        if (keep.Count == 0)
            return new CleanRule
            {
                Id = id, Group = "Navegadores", Section = section, Name = "Cookies", DefaultOn = false, Processes = procs,
                Warning = "Cerrará la sesión en los sitios web. Puedes proteger sitios en Opciones › Cookies a conservar.",
                Paths = files,
            };
        // Con lista blanca: se borran filas dentro de la base de datos en lugar del archivo completo
        return new CleanRule
        {
            Id = id, Group = "Navegadores", Section = section, Name = "Cookies (excepto los sitios protegidos)", DefaultOn = false, Processes = procs,
            CustomMeasure = () => (0, dbs().Sum(d => CookieKeeper.Process(d, keep, false))),
            CustomClean = () => { foreach (var d in dbs()) CookieKeeper.Process(d, keep, true); },
        };
    }

    // ----------------- Registro (privacidad) -----------------
    private static long CountRegistryValues(string subKey)
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(subKey);
            return k?.GetValueNames().Count(n => n.Length > 0) ?? 0;
        }
        catch { return 0; }
    }

    private static void ClearRegistryValues(string subKey)
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(subKey, true);
            if (k == null) return;
            foreach (var n in k.GetValueNames()) { try { k.DeleteValue(n); } catch { } }
            foreach (var n in k.GetSubKeyNames()) { try { k.DeleteSubKeyTree(n); } catch { } }
        }
        catch { }
    }

    // ----------------- Análisis y limpieza -----------------
    public static RuleResult Analyze(CleanRule rule, bool collectFiles, CancellationToken ct)
    {
        var res = new RuleResult { Rule = rule };
        if (rule.CustomMeasure != null)
        {
            var (b, c) = rule.CustomMeasure();
            res.Bytes = b; res.Count = c;
            return res;
        }
        if (rule.Paths == null) return res;
        var s = AppSettings.Current;
        var now = DateTime.Now;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var spec in rule.Paths())
        {
            if (string.IsNullOrWhiteSpace(spec.Dir) || !Directory.Exists(spec.Dir)) continue;
            foreach (var f in Util.SafeFiles(spec.Dir, spec.Pattern, spec.Recursive))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    if (!seen.Add(f.FullName)) continue;
                    if (s.IsExcluded(f.FullName)) continue;
                    if (spec.MinAgeHours > 0 && (now - f.LastWriteTime).TotalHours < spec.MinAgeHours) continue;
                    res.Bytes += f.Length;
                    res.Count++;
                    if (collectFiles) res.Files.Add(f.FullName);
                }
                catch { }
            }
        }
        return res;
    }

    public static RuleResult Clean(CleanRule rule, CancellationToken ct)
    {
        if (rule.CustomClean != null)
        {
            var before = Analyze(rule, false, ct);
            try { rule.CustomClean(); } catch { }
            return before;
        }
        var analysis = Analyze(rule, true, ct);
        var result = new RuleResult { Rule = rule };
        bool secure = AppSettings.Current.SecureDeleteOnClean;
        foreach (var file in analysis.Files)
        {
            ct.ThrowIfCancellationRequested();
            long size = 0;
            try { size = new FileInfo(file).Length; } catch { }
            if (Util.TryDeleteFile(file, secure)) { result.Bytes += size; result.Count++; }
            else result.Failed++;
        }
        if (rule.Paths != null)
        {
            foreach (var spec in rule.Paths().Where(p => p.RemoveEmptyDirs && p.Recursive && Directory.Exists(p.Dir)))
                Util.RemoveEmptyDirs(spec.Dir, removeRoot: false);
        }
        return result;
    }
}
