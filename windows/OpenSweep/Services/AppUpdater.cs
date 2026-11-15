using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using Microsoft.Win32;
using OpenSweep.Core;

namespace OpenSweep.Services;

public sealed record AppUpdate(Version Version, string PageUrl, string? SetupUrl, string? PortableUrl);

/// <summary>Comprueba si hay una versión nueva de OpenSweep en las releases de GitHub y la instala con un clic.</summary>
public static class AppUpdater
{
    private const string Api = "https://api.github.com/repos/jromo16/OpenSweep/releases/latest";
    private const string Releases = "https://github.com/jromo16/OpenSweep/";
    private const string InnoKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{6F3C2A1E-8B5D-4E7A-9C21-0A5B7E4D3F10}_is1";

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

    /// <summary>¿Este OpenSweep es el instalado con el instalador (y no el .exe portable)?</summary>
    public static bool IsInstalledCopy()
    {
        var exeDir = Path.GetDirectoryName(Environment.ProcessPath ?? "")?.TrimEnd('\\') ?? "";
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var k = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view).OpenSubKey(InnoKey);
                if (k?.GetValue("InstallLocation") is string dir && dir.TrimEnd('\\').Equals(exeDir, StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch { }
        }
        return false;
    }

    /// <summary>Descarga un archivo de las releases del proyecto y comprueba que es un ejecutable completo.</summary>
    private static async Task<bool> Download(string url, string file, IProgress<int> progress)
    {
        if (!url.StartsWith(Releases, StringComparison.OrdinalIgnoreCase)) return false;   // solo desde GitHub del proyecto
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("OpenSweep");
        using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
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
        var head = new byte[2];
        await using (var f = File.OpenRead(file)) await f.ReadExactlyAsync(head);
        return head[0] == 'M' && head[1] == 'Z' && (total == 0 || new FileInfo(file).Length == total);
    }

    /// <summary>Script que espera a que OpenSweep se cierre, reemplaza el .exe portable y lo vuelve a abrir.</summary>
    internal static string PortableSwapScript(int pid, string next, string exe) =>
        "@echo off\r\n" +
        ":wait\r\n" +
        $"tasklist /FI \"PID eq {pid}\" 2>nul | find \"{pid}\" >nul && (timeout /t 1 /nobreak >nul & goto wait)\r\n" +
        $"move /Y \"{next}\" \"{exe}\" >nul\r\n" +
        $"start \"\" \"{exe}\"\r\n" +
        "del \"%~f0\"\r\n";

    /// <summary>
    /// Descarga e instala la versión nueva. Si OpenSweep está instalado, ejecuta el instalador en silencio
    /// (cierra OpenSweep, actualiza y lo vuelve a abrir). Si es el .exe portable, lo reemplaza al cerrarse.
    /// Devuelve true si hay que cerrar OpenSweep para terminar.
    /// </summary>
    public static async Task<bool> InstallAsync(AppUpdate u, IProgress<int> progress)
    {
        try
        {
            if (IsInstalledCopy() || u.PortableUrl == null)
            {
                if (u.SetupUrl == null) { Util.OpenUrl(u.PageUrl); return false; }
                var setup = Path.Combine(Path.GetTempPath(), $"OpenSweep-Setup-{u.Version}.exe");
                if (!await Download(u.SetupUrl, setup, progress)) { Util.OpenUrl(u.PageUrl); return false; }
                Process.Start(new ProcessStartInfo(setup, "/SILENT /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS") { UseShellExecute = true });
                return true;
            }

            var exe = Environment.ProcessPath!;
            var next = exe + ".new";
            if (!await Download(u.PortableUrl, next, progress)) { Util.OpenUrl(u.PageUrl); return false; }
            var script = Path.Combine(Path.GetTempPath(), "opensweep-update.cmd");
            File.WriteAllText(script, PortableSwapScript(Environment.ProcessId, next, exe));
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{script}\"") { UseShellExecute = false, CreateNoWindow = true });
            Scheduler.Log($"Actualización portable a {u.Version} descargada");
            return true;
        }
        catch
        {
            Util.OpenUrl(u.PageUrl);
            return false;
        }
    }
}
