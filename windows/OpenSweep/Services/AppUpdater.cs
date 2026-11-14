using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using OpenSweep.Core;

namespace OpenSweep.Services;

public sealed record AppUpdate(Version Version, string PageUrl, string? SetupUrl, string? PortableUrl);

/// <summary>Comprueba si hay una versión nueva de OpenSweep en las releases de GitHub.</summary>
public static class AppUpdater
{
    private const string Api = "https://api.github.com/repos/jromo16/OpenSweep/releases/latest";

    public static Version Current => Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);

    public static async Task<AppUpdate?> CheckAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("OpenSweep");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            using var doc = JsonDocument.Parse(await http.GetStringAsync(Api));
            var root = doc.RootElement;
            var tag = root.GetProperty("tag_name").GetString()?.TrimStart('v') ?? "";
            if (!Version.TryParse(tag, out var remote)) return null;
            string? setup = null, portable = null;
            foreach (var a in root.GetProperty("assets").EnumerateArray())
            {
                var name = a.GetProperty("name").GetString() ?? "";
                var url = a.GetProperty("browser_download_url").GetString();
                if (name.EndsWith("Setup.exe", StringComparison.OrdinalIgnoreCase)) setup = url;
                else if (name.EndsWith("windows.exe", StringComparison.OrdinalIgnoreCase)) portable = url;
            }
            var cur = Current;
            var curNorm = new Version(cur.Major, cur.Minor, Math.Max(cur.Build, 0));
            var remNorm = new Version(remote.Major, remote.Minor, Math.Max(remote.Build, 0));
            return remNorm > curNorm ? new AppUpdate(remNorm, root.GetProperty("html_url").GetString() ?? "", setup, portable) : null;
        }
        catch { return null; }
    }

    /// <summary>Descarga el instalador y lo ejecuta en modo silencioso. Devuelve false si no fue posible.</summary>
    public static async Task<bool> InstallAsync(AppUpdate u, IProgress<int> progress)
    {
        if (u.SetupUrl == null) { Util.OpenUrl(u.PageUrl); return false; }
        try
        {
            var file = Path.Combine(Path.GetTempPath(), $"OpenSweep-Setup-{u.Version}.exe");
            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("OpenSweep");
            using var resp = await http.GetAsync(u.SetupUrl, HttpCompletionOption.ResponseHeadersRead);
            resp.EnsureSuccessStatusCode();
            var total = resp.Content.Headers.ContentLength ?? 0;
            await using (var src = await resp.Content.ReadAsStreamAsync())
            await using (var dst = File.Create(file))
            {
                var buf = new byte[81920];
                long done = 0; int n;
                while ((n = await src.ReadAsync(buf)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, n));
                    done += n;
                    if (total > 0) progress.Report((int)(done * 100 / total));
                }
            }
            // El instalador cierra OpenSweep, actualiza y lo vuelve a abrir
            Process.Start(new ProcessStartInfo(file, "/SILENT /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS") { UseShellExecute = true });
            return true;
        }
        catch
        {
            Util.OpenUrl(u.PageUrl);
            return false;
        }
    }
}
