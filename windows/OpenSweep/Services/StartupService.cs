using System.Text.Json;
using OpenSweep.Core;
using Microsoft.Win32;

namespace OpenSweep.Services;

public enum StartupKind { RegistryRun, StartupFolder, ScheduledTask }

public sealed class StartupEntry
{
    public required string Name { get; init; }
    public required string Command { get; init; }
    public required string Location { get; init; }
    public required StartupKind Kind { get; init; }
    public bool Enabled { get; set; }
    public string Publisher { get; init; } = "";

    // Datos internos
    public RegistryHive Hive { get; init; }
    public RegistryView View { get; init; }
    public string RegPath { get; init; } = "";
    public string ApprovedKey { get; init; } = "";   // subclave de StartupApproved (Run, Run32, StartupFolder)
    public string FilePath { get; init; } = "";
    public string TaskPath { get; init; } = "";

    public string KindText => Kind switch
    {
        StartupKind.RegistryRun => "Registro",
        StartupKind.StartupFolder => "Carpeta Inicio",
        _ => "Tarea programada"
    };
    public string StateText => Enabled ? "Habilitado" : "Deshabilitado";
}

public sealed class ScheduledTaskJson
{
    public string TaskName { get; set; } = "";
    public string TaskPath { get; set; } = "";
    public int State { get; set; }
    public string? Author { get; set; }
    public string? Exec { get; set; }
}

public static class StartupService
{
    private const string ApprovedBase = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved";

    public static List<StartupEntry> GetEntries(bool includeTasks, bool includeMicrosoftTasks)
    {
        var list = new List<StartupEntry>();

        foreach (var (hive, view, path, approved, label) in new[]
        {
            (RegistryHive.CurrentUser, RegistryView.Registry64, @"Software\Microsoft\Windows\CurrentVersion\Run", "Run", "HKCU\\Run"),
            (RegistryHive.LocalMachine, RegistryView.Registry64, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "Run", "HKLM\\Run"),
            (RegistryHive.LocalMachine, RegistryView.Registry32, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "Run32", "HKLM\\Run (32 bits)"),
            (RegistryHive.CurrentUser, RegistryView.Registry64, @"Software\Microsoft\Windows\CurrentVersion\RunOnce", "", "HKCU\\RunOnce"),
            (RegistryHive.LocalMachine, RegistryView.Registry64, @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce", "", "HKLM\\RunOnce"),
        })
        {
            try
            {
                using var k = RegistryKey.OpenBaseKey(hive, view).OpenSubKey(path);
                if (k == null) continue;
                foreach (var v in k.GetValueNames())
                {
                    if (v.Length == 0) continue;
                    var cmd = k.GetValue(v)?.ToString() ?? "";
                    list.Add(new StartupEntry
                    {
                        Name = v, Command = cmd, Location = label, Kind = StartupKind.RegistryRun,
                        Hive = hive, View = view, RegPath = path, ApprovedKey = approved,
                        Enabled = approved.Length == 0 || IsApproved(hive, approved, v),
                        Publisher = GetPublisher(PathHelper.ExtractPath(cmd)),
                    });
                }
            }
            catch { }
        }

        foreach (var (dir, hive, label) in new[]
        {
            (Environment.GetFolderPath(Environment.SpecialFolder.Startup), RegistryHive.CurrentUser, "Inicio (usuario)"),
            (Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), RegistryHive.LocalMachine, "Inicio (todos)"),
        })
        {
            foreach (var f in Util.SafeFiles(dir, "*", false))
            {
                if (f.Name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                list.Add(new StartupEntry
                {
                    Name = Path.GetFileNameWithoutExtension(f.Name), Command = f.FullName, Location = label, Kind = StartupKind.StartupFolder,
                    Hive = hive, ApprovedKey = "StartupFolder", FilePath = f.FullName,
                    Enabled = IsApproved(hive, "StartupFolder", f.Name),
                });
            }
        }

        if (includeTasks) list.AddRange(GetTasks(includeMicrosoftTasks));
        return list;
    }

    private static List<StartupEntry> GetTasks(bool includeMicrosoft)
    {
        var filter = includeMicrosoft ? "" : "| Where-Object { $_.TaskPath -notlike '\\Microsoft\\*' }";
        var (code, output) = Util.PowerShell(
            $"Get-ScheduledTask {filter} | ForEach-Object {{ [pscustomobject]@{{ TaskName=$_.TaskName; TaskPath=$_.TaskPath; State=[int]$_.State; Author=$_.Author; " +
            "Exec=(($_.Actions | Where-Object { $_.Execute } | ForEach-Object { ($_.Execute + ' ' + $_.Arguments).Trim() }) -join ' ; ') } } | ConvertTo-Json -Compress", 120_000);
        var result = new List<StartupEntry>();
        if (code != 0) return result;
        try
        {
            var json = output.Trim();
            var start = json.IndexOfAny(['[', '{']);
            if (start < 0) return result;
            json = json[start..];
            List<ScheduledTaskJson> tasks = json.StartsWith('[')
                ? JsonSerializer.Deserialize<List<ScheduledTaskJson>>(json) ?? []
                : [JsonSerializer.Deserialize<ScheduledTaskJson>(json)!];
            foreach (var t in tasks)
            {
                result.Add(new StartupEntry
                {
                    Name = t.TaskName, Command = t.Exec ?? "", Location = t.TaskPath, Kind = StartupKind.ScheduledTask,
                    TaskPath = t.TaskPath, Enabled = t.State != 1, // 1 = Disabled
                    Publisher = t.Author ?? "",
                });
            }
        }
        catch { }
        return result;
    }

    private static string GetPublisher(string? exe)
    {
        try
        {
            if (exe != null && File.Exists(exe))
                return System.Diagnostics.FileVersionInfo.GetVersionInfo(exe).CompanyName ?? "";
        }
        catch { }
        return "";
    }

    /// <summary>Windows guarda el estado habilitado/deshabilitado en StartupApproved (byte 0: 2/6 = habilitado, 3/7 = deshabilitado).</summary>
    private static bool IsApproved(RegistryHive hive, string approvedKey, string valueName)
    {
        try
        {
            using var k = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64).OpenSubKey($@"{ApprovedBase}\{approvedKey}");
            if (k?.GetValue(valueName) is byte[] data && data.Length > 0) return (data[0] & 1) == 0;
        }
        catch { }
        return true;
    }

    public static bool SetEnabled(StartupEntry e, bool enabled)
    {
        try
        {
            if (e.Kind == StartupKind.ScheduledTask)
            {
                var verb = enabled ? "Enable-ScheduledTask" : "Disable-ScheduledTask";
                var (code, _) = Util.PowerShell($"{verb} -TaskName '{e.Name.Replace("'", "''")}' -TaskPath '{e.TaskPath.Replace("'", "''")}' -ErrorAction Stop | Out-Null");
                if (code == 0) e.Enabled = enabled;
                return code == 0;
            }
            if (string.IsNullOrEmpty(e.ApprovedKey)) return false;
            var valueName = e.Kind == StartupKind.StartupFolder ? Path.GetFileName(e.FilePath) : e.Name;
            using var k = RegistryKey.OpenBaseKey(e.Hive, RegistryView.Registry64).CreateSubKey($@"{ApprovedBase}\{e.ApprovedKey}", true);
            var data = new byte[12];
            data[0] = enabled ? (byte)2 : (byte)3;
            if (!enabled) BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()).CopyTo(data, 4);
            k.SetValue(valueName, data, RegistryValueKind.Binary);
            e.Enabled = enabled;
            return true;
        }
        catch { return false; }
    }

    public static bool Delete(StartupEntry e)
    {
        try
        {
            switch (e.Kind)
            {
                case StartupKind.RegistryRun:
                    using (var k = RegistryKey.OpenBaseKey(e.Hive, e.View).OpenSubKey(e.RegPath, true))
                        k?.DeleteValue(e.Name, false);
                    break;
                case StartupKind.StartupFolder:
                    File.Delete(e.FilePath);
                    break;
                case StartupKind.ScheduledTask:
                    var (code, _) = Util.PowerShell($"Unregister-ScheduledTask -TaskName '{e.Name.Replace("'", "''")}' -TaskPath '{e.TaskPath.Replace("'", "''")}' -Confirm:$false -ErrorAction Stop");
                    return code == 0;
            }
            if (!string.IsNullOrEmpty(e.ApprovedKey))
            {
                using var ak = RegistryKey.OpenBaseKey(e.Hive, RegistryView.Registry64).OpenSubKey($@"{ApprovedBase}\{e.ApprovedKey}", true);
                ak?.DeleteValue(e.Kind == StartupKind.StartupFolder ? Path.GetFileName(e.FilePath) : e.Name, false);
            }
            return true;
        }
        catch { return false; }
    }
}
