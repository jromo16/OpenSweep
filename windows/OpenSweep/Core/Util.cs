using System.Diagnostics;
using System.Text;

namespace OpenSweep.Core;

public static class Util
{
    public static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double v = bytes;
        int i = 0;
        while (v >= 1024 && i < units.Length - 1) { v /= 1024; i++; }
        return i == 0 ? $"{bytes} B" : $"{v:0.##} {units[i]}";
    }

    public static readonly EnumerationOptions AllFilesRecursive = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
        ReturnSpecialDirectories = false,
    };

    public static readonly EnumerationOptions AllFilesTop = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        AttributesToSkip = FileAttributes.ReparsePoint,
        ReturnSpecialDirectories = false,
    };

    /// <summary>Enumera archivos sin lanzar excepciones por permisos o rutas inexistentes.</summary>
    public static IEnumerable<FileInfo> SafeFiles(string dir, string pattern = "*", bool recursive = true)
    {
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) yield break;
        IEnumerator<FileInfo>? e = null;
        try { e = new DirectoryInfo(dir).EnumerateFiles(pattern, recursive ? AllFilesRecursive : AllFilesTop).GetEnumerator(); }
        catch { yield break; }
        while (true)
        {
            FileInfo? cur;
            try
            {
                if (!e.MoveNext()) break;
                cur = e.Current;
            }
            catch { break; }
            yield return cur;
        }
        e.Dispose();
    }

    public static IEnumerable<DirectoryInfo> SafeDirs(string dir, bool recursive = false)
    {
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) yield break;
        IEnumerator<DirectoryInfo>? e = null;
        try { e = new DirectoryInfo(dir).EnumerateDirectories("*", recursive ? AllFilesRecursive : AllFilesTop).GetEnumerator(); }
        catch { yield break; }
        while (true)
        {
            DirectoryInfo? cur;
            try
            {
                if (!e.MoveNext()) break;
                cur = e.Current;
            }
            catch { break; }
            yield return cur;
        }
        e.Dispose();
    }

    public static long DirSize(string dir)
    {
        long total = 0;
        foreach (var f in SafeFiles(dir))
        {
            try { total += f.Length; } catch { }
        }
        return total;
    }

    /// <summary>Elimina un archivo quitando atributos de solo lectura. Devuelve true si se borró.</summary>
    public static bool TryDeleteFile(string path, bool secure = false)
    {
        try
        {
            if (!File.Exists(path)) return true;
            var attr = File.GetAttributes(path);
            if ((attr & (FileAttributes.ReadOnly | FileAttributes.System | FileAttributes.Hidden)) != 0)
                File.SetAttributes(path, FileAttributes.Normal);
            if (secure) SecureWipe.OverwriteFile(path, 1);
            File.Delete(path);
            return true;
        }
        catch { return false; }
    }

    public static bool TryDeleteDirectory(string path)
    {
        try
        {
            if (!Directory.Exists(path)) return true;
            foreach (var f in SafeFiles(path)) TryDeleteFile(f.FullName);
            RemoveEmptyDirs(path, removeRoot: true);
            return !Directory.Exists(path);
        }
        catch { return false; }
    }

    /// <summary>Elimina subcarpetas vacías (de abajo hacia arriba).</summary>
    public static void RemoveEmptyDirs(string root, bool removeRoot = false)
    {
        if (!Directory.Exists(root)) return;
        foreach (var d in SafeDirs(root).ToList())
        {
            RemoveEmptyDirs(d.FullName, true);
        }
        if (removeRoot)
        {
            try
            {
                if (!Directory.EnumerateFileSystemEntries(root).Any())
                {
                    var di = new DirectoryInfo(root);
                    di.Attributes = FileAttributes.Normal;
                    di.Delete(false);
                }
            }
            catch { }
        }
    }

    public static string Env(string name) => Environment.GetEnvironmentVariable(name) ?? "";

    public static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    public static string RoamingAppData => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    public static string ProgramData => Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
    public static string WinDir => Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    public static string UserProfile => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>Ejecuta un proceso y devuelve su salida estándar.</summary>
    public static (int exitCode, string output) Run(string file, string args, int timeoutMs = 120_000, Encoding? encoding = null)
    {
        try
        {
            var psi = new ProcessStartInfo(file, args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = encoding ?? Encoding.UTF8,
                StandardErrorEncoding = encoding ?? Encoding.UTF8,
            };
            using var p = Process.Start(psi)!;
            var stdout = p.StandardOutput.ReadToEndAsync();
            var stderr = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(timeoutMs)) { try { p.Kill(true); } catch { } }
            return (p.HasExited ? p.ExitCode : -1, stdout.Result + stderr.Result);
        }
        catch (Exception ex)
        {
            return (-1, ex.Message);
        }
    }

    /// <summary>Ejecuta un script de PowerShell y devuelve la salida (UTF-8).</summary>
    public static (int exitCode, string output) PowerShell(string script, int timeoutMs = 120_000)
    {
        var full = "[Console]::OutputEncoding=[System.Text.Encoding]::UTF8; $ProgressPreference='SilentlyContinue'; " + script;
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(full));
        return Run("powershell.exe", $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}", timeoutMs);
    }

    public static void OpenInExplorer(string path)
    {
        try
        {
            if (File.Exists(path)) Process.Start("explorer.exe", $"/select,\"{path}\"");
            else if (Directory.Exists(path)) Process.Start("explorer.exe", $"\"{path}\"");
        }
        catch { }
    }

    public static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }

    public static string Normalize(string s)
    {
        var sb = new StringBuilder();
        foreach (var c in s.ToLowerInvariant())
            if (char.IsLetterOrDigit(c)) sb.Append(c);
        return sb.ToString();
    }

    public static bool IsProcessRunning(params string[] names)
    {
        foreach (var n in names)
        {
            try { if (Process.GetProcessesByName(n).Length > 0) return true; } catch { }
        }
        return false;
    }

    public static void KillProcesses(params string[] names)
    {
        foreach (var n in names)
        {
            try
            {
                foreach (var p in Process.GetProcessesByName(n))
                {
                    try { p.Kill(true); p.WaitForExit(3000); } catch { }
                }
            }
            catch { }
        }
    }
}
