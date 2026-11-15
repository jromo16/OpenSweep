using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Win32;
using OpenSweep.Core;

namespace OpenSweep.Services;

public sealed record AppUpdate(Version Version, string PageUrl, string? SetupUrl, string? PortableUrl,
    string? SetupSha256 = null, string? PortableSha256 = null);

/// <summary>Comprueba si hay una versión nueva de OpenSweep en las releases de GitHub y la instala con un clic.</summary>
public static class AppUpdater
{
    private const string Api = "https://api.github.com/repos/jromo16/OpenSweep/releases/latest";
    private const string Releases = "https://github.com/jromo16/OpenSweep/";
    private const string InnoKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{6F3C2A1E-8B5D-4E7A-9C21-0A5B7E4D3F10}_is1";

    public static Version Current => Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);

    /// <summary>Motivo del último fallo al comprobar (sin conexión, límite de GitHub…); null si la comprobación fue bien.</summary>
    public static string? LastError { get; private set; }

    public static async Task<AppUpdate?> CheckAsync()
    {
        LastError = null;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("OpenSweep");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            using var doc = JsonDocument.Parse(await http.GetStringAsync(Api));
            return Parse(doc.RootElement, Current);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return null;
        }
    }

    /// <summary>Lee la respuesta de GitHub. Devuelve la actualización si la versión publicada es más nueva.</summary>
    internal static AppUpdate? Parse(JsonElement root, Version current)
    {
        var tag = root.GetProperty("tag_name").GetString()?.TrimStart('v') ?? "";
        if (!Version.TryParse(tag, out var remote)) return null;
        string? setup = null, portable = null, setupSha = null, portableSha = null;
        foreach (var a in root.GetProperty("assets").EnumerateArray())
        {
            var name = a.GetProperty("name").GetString() ?? "";
            var url = a.GetProperty("browser_download_url").GetString();
            // GitHub publica la huella de cada archivo: «sha256:…»
            var digest = a.TryGetProperty("digest", out var d) && d.GetString() is { } ds && ds.StartsWith("sha256:") ? ds[7..] : null;
            if (name.EndsWith("Setup.exe", StringComparison.OrdinalIgnoreCase)) { setup = url; setupSha = digest; }
            else if (name.EndsWith("windows.exe", StringComparison.OrdinalIgnoreCase)) { portable = url; portableSha = digest; }
        }
        var curNorm = new Version(current.Major, current.Minor, Math.Max(current.Build, 0));
        var remNorm = new Version(remote.Major, remote.Minor, Math.Max(remote.Build, 0));
        return remNorm > curNorm
            ? new AppUpdate(remNorm, root.GetProperty("html_url").GetString() ?? "", setup, portable, setupSha, portableSha)
            : null;
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

    /// <summary>
    /// Carpeta de descargas que solo pueden modificar los administradores y el sistema: un programa sin permisos
    /// no puede cambiar el instalador entre la descarga y su ejecución (que es con permisos de administrador).
    /// </summary>
    private static string SecureFolder()
    {
        var dir = Path.Combine(Util.ProgramData, "OpenSweep", "Actualizaciones");
        var di = Directory.CreateDirectory(dir);
        var sec = new DirectorySecurity();
        sec.SetAccessRuleProtection(true, false);   // sin permisos heredados
        foreach (var sid in new[] { WellKnownSidType.BuiltinAdministratorsSid, WellKnownSidType.LocalSystemSid })
            sec.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid, null), FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        di.SetAccessControl(sec);
        foreach (var old in di.GetFiles()) try { old.Delete(); } catch { }
        return dir;
    }

    /// <summary>Descarga un archivo de las releases del proyecto y comprueba que es un ejecutable completo y sin alterar.</summary>
    private static async Task<bool> Download(string url, string? sha256, string file, IProgress<int> progress)
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
        return Verify(file, total, sha256);
    }

    /// <summary>Ejecutable de Windows completo y, si GitHub dio su huella, idéntico al publicado.</summary>
    internal static bool Verify(string file, long expectedLength, string? sha256)
    {
        var info = new FileInfo(file);
        if (!info.Exists || info.Length < 2 || (expectedLength > 0 && info.Length != expectedLength)) return false;
        using var f = File.OpenRead(file);
        if (f.ReadByte() != 'M' || f.ReadByte() != 'Z') return false;
        if (sha256 == null) return true;
        f.Position = 0;
        return Convert.ToHexString(SHA256.HashData(f)).Equals(sha256, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Quita el .exe anterior que dejó una actualización portable (se llama al arrancar).</summary>
    public static void CleanupPortableUpdate()
    {
        try
        {
            var old = Environment.ProcessPath + ".old";
            if (File.Exists(old)) File.Delete(old);
        }
        catch { }
    }

    /// <summary>
    /// Descarga e instala la versión nueva. Si OpenSweep está instalado, ejecuta el instalador en silencio
    /// (cierra OpenSweep, actualiza y lo vuelve a abrir). Si es el .exe portable, lo reemplaza y lo vuelve a abrir.
    /// Devuelve true si hay que cerrar OpenSweep para terminar.
    /// </summary>
    public static async Task<bool> InstallAsync(AppUpdate u, IProgress<int> progress)
    {
        try
        {
            if (IsInstalledCopy() || u.PortableUrl == null)
            {
                if (u.SetupUrl == null) { Util.OpenUrl(u.PageUrl); return false; }
                var setup = Path.Combine(SecureFolder(), $"OpenSweep-Setup-{u.Version}.exe");
                if (!await Download(u.SetupUrl, u.SetupSha256, setup, progress)) { Util.OpenUrl(u.PageUrl); return false; }
                Process.Start(new ProcessStartInfo(setup, "/SILENT /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS") { UseShellExecute = true });
                return true;
            }

            // Portable: Windows deja renombrar un .exe en uso. Se aparta el actual (.old), se pone el nuevo en su sitio
            // y se abre. Sin scripts intermedios que se puedan alterar.
            var exe = Environment.ProcessPath!;
            var download = Path.Combine(SecureFolder(), $"OpenSweep-{u.Version}.exe");
            if (!await Download(u.PortableUrl, u.PortableSha256, download, progress)) { Util.OpenUrl(u.PageUrl); return false; }
            var old = exe + ".old";
            if (File.Exists(old)) File.Delete(old);
            File.Move(exe, old);
            try { File.Move(download, exe); }
            catch { File.Move(old, exe); throw; }   // si falla, se deja todo como estaba

            // El monitor de la bandeja usa el mismo .exe: se cierra y se vuelve a abrir con la versión nueva
            try { if (EventWaitHandle.TryOpenExisting(Scheduler.TrayStopEvent, out var h)) { h.Set(); h.Dispose(); } } catch { }
            if (AppSettings.Current.TrayMonitor)
                Process.Start(new ProcessStartInfo(exe, "--tray") { UseShellExecute = true });
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
            Scheduler.Log($"Actualización portable a {u.Version} instalada");
            return true;
        }
        catch
        {
            Util.OpenUrl(u.PageUrl);
            return false;
        }
    }
}
