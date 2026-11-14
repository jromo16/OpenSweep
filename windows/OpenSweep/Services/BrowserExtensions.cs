using System.Text.Json;
using OpenSweep.Core;

namespace OpenSweep.Services;

public sealed class BrowserExtension
{
    public required string Browser { get; init; }
    public required string Profile { get; init; }
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string Version { get; init; } = "";
    public string Description { get; init; } = "";
    public required string Folder { get; init; }
    public string[] Processes { get; init; } = [];
}

public static class BrowserExtensions
{
    public static List<BrowserExtension> Scan()
    {
        var L = Util.LocalAppData; var R = Util.RoamingAppData;
        var browsers = new (string name, string userData, string[] procs)[]
        {
            ("Google Chrome", Path.Combine(L, @"Google\Chrome\User Data"), ["chrome"]),
            ("Microsoft Edge", Path.Combine(L, @"Microsoft\Edge\User Data"), ["msedge"]),
            ("Brave", Path.Combine(L, @"BraveSoftware\Brave-Browser\User Data"), ["brave"]),
            ("Vivaldi", Path.Combine(L, @"Vivaldi\User Data"), ["vivaldi"]),
            ("Opera", Path.Combine(R, @"Opera Software\Opera Stable"), ["opera"]),
        };
        var list = new List<BrowserExtension>();
        foreach (var (name, ud, procs) in browsers)
        {
            foreach (var profile in JunkCleaner.ChromiumProfiles(ud))
            {
                var extRoot = Path.Combine(profile, "Extensions");
                foreach (var ext in Util.SafeDirs(extRoot))
                {
                    var versionDir = Util.SafeDirs(ext.FullName).OrderByDescending(d => d.Name).FirstOrDefault();
                    if (versionDir == null) continue;
                    var manifest = Path.Combine(versionDir.FullName, "manifest.json");
                    if (!File.Exists(manifest)) continue;
                    try
                    {
                        using var doc = JsonDocument.Parse(File.ReadAllText(manifest), new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
                        var root = doc.RootElement;
                        var extName = Resolve(root.TryGetProperty("name", out var n) ? n.GetString() ?? ext.Name : ext.Name, versionDir.FullName, root);
                        var desc = Resolve(root.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "", versionDir.FullName, root);
                        // Extensiones internas del navegador (sin nombre real)
                        if (extName.StartsWith("__MSG_")) continue;
                        list.Add(new BrowserExtension
                        {
                            Browser = name,
                            Profile = Path.GetFileName(profile),
                            Id = ext.Name,
                            Name = extName,
                            Version = root.TryGetProperty("version", out var v) ? v.GetString() ?? "" : "",
                            Description = desc,
                            Folder = ext.FullName,
                            Processes = procs,
                        });
                    }
                    catch { }
                }
            }
        }

        // Firefox: extensions.json
        foreach (var profile in Util.SafeDirs(Path.Combine(R, @"Mozilla\Firefox\Profiles")))
        {
            var file = Path.Combine(profile.FullName, "extensions.json");
            if (!File.Exists(file)) continue;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                foreach (var addon in doc.RootElement.GetProperty("addons").EnumerateArray())
                {
                    if (addon.TryGetProperty("location", out var loc) && loc.GetString() != "app-profile") continue;
                    var id = addon.GetProperty("id").GetString() ?? "";
                    var path = addon.TryGetProperty("path", out var p) ? p.GetString() ?? "" : "";
                    var dl = addon.TryGetProperty("defaultLocale", out var l) ? l : default;
                    var nm = dl.ValueKind == JsonValueKind.Object && dl.TryGetProperty("name", out var nn) ? nn.GetString() ?? id : id;
                    var ds = dl.ValueKind == JsonValueKind.Object && dl.TryGetProperty("description", out var dd) ? dd.GetString() ?? "" : "";
                    list.Add(new BrowserExtension
                    {
                        Browser = "Mozilla Firefox", Profile = profile.Name, Id = id, Name = nm, Description = ds,
                        Version = addon.TryGetProperty("version", out var vv) ? vv.GetString() ?? "" : "",
                        Folder = path, Processes = ["firefox"],
                    });
                }
            }
            catch { }
        }
        return list.OrderBy(e => e.Browser).ThenBy(e => e.Name).ToList();
    }

    private static string Resolve(string text, string extDir, JsonElement manifest)
    {
        if (!text.StartsWith("__MSG_")) return text;
        var key = text[6..].TrimEnd('_');
        var locales = new List<string> { "es", "es_419", "en", "en_US" };
        if (manifest.TryGetProperty("default_locale", out var dl) && dl.GetString() is { } d) locales.Add(d);
        foreach (var loc in locales)
        {
            var file = Path.Combine(extDir, "_locales", loc, "messages.json");
            if (!File.Exists(file)) continue;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file), new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
                foreach (var prop in doc.RootElement.EnumerateObject())
                    if (prop.Name.Equals(key, StringComparison.OrdinalIgnoreCase) && prop.Value.TryGetProperty("message", out var m))
                        return m.GetString() ?? text;
            }
            catch { }
        }
        return text;
    }

    public static bool Remove(BrowserExtension e)
    {
        if (string.IsNullOrEmpty(e.Folder)) return false;
        if (File.Exists(e.Folder)) return Util.TryDeleteFile(e.Folder); // Firefox .xpi
        return Util.TryDeleteDirectory(e.Folder);
    }
}
