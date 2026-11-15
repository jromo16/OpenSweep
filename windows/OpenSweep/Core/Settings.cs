using System.Text.Json;

namespace OpenSweep.Core;

public sealed class AppSettings
{
    public List<string> Exclusions { get; set; } = [];
    public List<string> CustomFolders { get; set; } = [];
    public bool SecureDeleteOnClean { get; set; }
    public bool CloseBrowsersAutomatically { get; set; }
    public bool CreateRestorePointBeforeUninstall { get; set; } = true;
    public bool BackupRegistryBeforeFix { get; set; } = true;
    public int TempMinAgeHours { get; set; } = 24;
    public Dictionary<string, bool> CleanerSelection { get; set; } = [];
    public List<string> CookieWhitelist { get; set; } = [];
    public string AutoClean { get; set; } = "never";      // never | daily | weekly
    public bool TrayMonitor { get; set; }
    /// <summary>Limpiar cada navegador al cerrarlo (lo hace el monitor de la bandeja).</summary>
    public bool CleanBrowsersOnClose { get; set; }
    /// <summary>Comentarios del usuario sobre programas instalados (clave: InstalledProgram.Id).</summary>
    public Dictionary<string, string> ProgramComments { get; set; } = [];
    /// <summary>Programas ocultos en la lista del desinstalador (clave: InstalledProgram.Id).</summary>
    public List<string> HiddenPrograms { get; set; } = [];
    public int TrayThresholdMb { get; set; } = 500;
    public bool CheckUpdates { get; set; } = true;
    public string Language { get; set; } = "auto";
    public string Theme { get; set; } = "auto";           // auto | light | dark        // auto | es | en

    public static string Folder => Path.Combine(Util.RoamingAppData, "OpenSweep");
    public static string FilePath => Path.Combine(Folder, "settings.json");
    public static string BackupFolder => Path.Combine(Util.Env("USERPROFILE"), "Documents", "OpenSweep Backups");

    private static AppSettings? _current;
    private static DateTime _loadedStamp;
    public static AppSettings Current => _current ??= Load();

    /// <summary>Vuelve a leer el archivo si otro proceso de OpenSweep lo cambió (lo usa el monitor de la bandeja).</summary>
    public static void ReloadIfChanged()
    {
        try
        {
            var stamp = File.Exists(FilePath) ? File.GetLastWriteTimeUtc(FilePath) : default;
            if (_current != null && stamp == _loadedStamp) return;
            _current = Load();
        }
        catch { }
    }

    private static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                _loadedStamp = File.GetLastWriteTimeUtc(FilePath);
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch { }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    public bool IsExcluded(string path) =>
        Exclusions.Any(e => !string.IsNullOrWhiteSpace(e) && path.StartsWith(e.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));
}
