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

    public static string Folder => Path.Combine(Util.RoamingAppData, "OpenSweep");
    public static string FilePath => Path.Combine(Folder, "settings.json");
    public static string BackupFolder => Path.Combine(Util.Env("USERPROFILE"), "Documents", "OpenSweep Backups");

    private static AppSettings? _current;
    public static AppSettings Current => _current ??= Load();

    private static AppSettings Load()
    {
        try
        {
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
