using OpenSweep.Core;

namespace OpenSweep.Services;

/// <summary>
/// Tareas programadas de Windows para la limpieza automática y el monitor de la bandeja.
/// Se ejecutan con privilegios elevados sin mostrar el aviso de UAC.
/// </summary>
public static class Scheduler
{
    private const string Folder = @"\OpenSweep\";
    private const string AutoCleanTask = "Limpieza automatica";
    private const string TrayTask = "Monitor";

    private static string Exe => Environment.ProcessPath ?? "";

    private static string Esc(string s) => s.Replace("'", "''");

    /// <summary>Señal para cerrar el monitor de la bandeja cuando se desactiva desde Opciones.</summary>
    public const string TrayStopEvent = @"Local\OpenSweepTrayStop";

    private static bool Register(string name, string trigger, string args, bool noTimeLimit = false)
    {
        // El monitor de la bandeja dura toda la sesión: sin límite de tiempo (PT0S). La limpieza, 2 horas como mucho.
        var limit = noTimeLimit ? "New-TimeSpan -Seconds 0" : "New-TimeSpan -Hours 2";
        var script =
            $"$a = New-ScheduledTaskAction -Execute '{Esc(Exe)}' -Argument '{args}'; " +
            $"$t = {trigger}; " +
            "$p = New-ScheduledTaskPrincipal -UserId \"$env:USERDOMAIN\\$env:USERNAME\" -LogonType Interactive -RunLevel Highest; " +
            $"$s = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable -ExecutionTimeLimit ({limit}); " +
            $"Register-ScheduledTask -TaskPath '{Folder}' -TaskName '{name}' -Action $a -Trigger $t -Principal $p -Settings $s -Force | Out-Null";
        return Util.PowerShell(script).exitCode == 0;
    }

    private static void Unregister(string name) =>
        Util.PowerShell($"Unregister-ScheduledTask -TaskPath '{Folder}' -TaskName '{name}' -Confirm:$false -ErrorAction SilentlyContinue");

    /// <summary>mode: never | daily | weekly</summary>
    public static bool SetAutoClean(string mode)
    {
        if (mode == "never") { Unregister(AutoCleanTask); return true; }
        var trigger = mode == "weekly"
            ? "New-ScheduledTaskTrigger -Weekly -DaysOfWeek Sunday -At 12:00"
            : "New-ScheduledTaskTrigger -Daily -At 12:00";
        return Register(AutoCleanTask, trigger, "--auto-clean");
    }

    public static bool SetTray(bool enabled)
    {
        if (!enabled)
        {
            Unregister(TrayTask);
            // Cerrar también el monitor que está abierto ahora mismo
            try { if (EventWaitHandle.TryOpenExisting(TrayStopEvent, out var h)) { h.Set(); h.Dispose(); } } catch { }
            return true;
        }
        return Register(TrayTask, "New-ScheduledTaskTrigger -AtLogOn -User \"$env:USERDOMAIN\\$env:USERNAME\"", "--tray", noTimeLimit: true);
    }

    public static string LogFile => Path.Combine(AppSettings.Folder, "historial.log");

    public static void Log(string line)
    {
        try
        {
            Directory.CreateDirectory(AppSettings.Folder);
            File.AppendAllText(LogFile, $"{DateTime.Now:yyyy-MM-dd HH:mm}  {line}{Environment.NewLine}");
        }
        catch { }
    }

    /// <summary>Reglas usadas por la limpieza automática: las seleccionadas, sin privacidad ni programas abiertos.</summary>
    private static List<CleanRule> AutoRules() =>
        JunkCleaner.BuildRules().Where(r => r.Selected && r.Group != "Privacidad" && r.Id != "win.recycle").ToList();

    public static long MeasureJunk() =>
        AutoRules().Sum(r => { try { return JunkCleaner.Analyze(r, false, CancellationToken.None).Bytes; } catch { return 0L; } });

    /// <summary>Limpieza silenciosa. Omite las reglas de programas que estén abiertos.</summary>
    public static long RunAutoClean()
    {
        long freed = 0;
        foreach (var r in AutoRules())
        {
            if (r.Processes.Length > 0 && Util.IsProcessRunning(r.Processes)) continue;
            try { freed += JunkCleaner.Clean(r, CancellationToken.None).Bytes; } catch { }
        }
        Log($"Limpieza automática: {Util.FormatBytes(freed)} liberados");
        return freed;
    }
}
