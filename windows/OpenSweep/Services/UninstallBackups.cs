using System.Text.Json;
using OpenSweep.Core;

namespace OpenSweep.Services;

/// <summary>Todo lo que se eliminó al desinstalar un programa, para poder deshacerlo (como el gestor de copias de Revo).</summary>
public sealed class UninstallRecord
{
    public string Program { get; set; } = "";
    public DateTime Date { get; set; } = DateTime.Now;
    public List<string> RegFiles { get; set; } = [];
    public List<string[]> Tasks { get; set; } = [];          // [ruta de la tarea, archivo XML]
    public List<string> Recycled { get; set; } = [];          // rutas originales enviadas a la papelera
    public int RegistryItems { get; set; }
    public int OtherItems { get; set; }                        // servicios y reglas del firewall (van en el .reg)
    public bool Restored { get; set; }

    public string FileName { get; set; } = "";
    public string DateText => Date.ToString("dd/MM/yyyy HH:mm");
    public string Summary =>
        $"{RegistryItems} {Loc.T("del registro")} · {Recycled.Count} {Loc.T("archivos y carpetas")}" +
        (Tasks.Count + OtherItems > 0 ? $" · {Tasks.Count + OtherItems} {Loc.T("servicios, tareas o reglas")}" : "");
    public string StateText => Restored ? Loc.T("Restaurada") : Loc.T("Se puede deshacer");
}

public static class UninstallBackups
{
    public static string Folder => Path.Combine(AppSettings.BackupFolder, "Desinstalaciones");

    public static void Save(UninstallRecord r)
    {
        Directory.CreateDirectory(Folder);
        if (r.FileName.Length == 0)
        {
            var safe = string.Concat(r.Program.Where(c => !Path.GetInvalidFileNameChars().Contains(c)));
            r.FileName = Path.Combine(Folder, $"{r.Date:yyyyMMdd_HHmmss}_{safe}.json");
        }
        File.WriteAllText(r.FileName, JsonSerializer.Serialize(r, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static List<UninstallRecord> LoadAll()
    {
        var list = new List<UninstallRecord>();
        foreach (var f in Util.SafeFiles(Folder, "*.json", false))
        {
            try
            {
                var r = JsonSerializer.Deserialize<UninstallRecord>(File.ReadAllText(f.FullName));
                if (r != null) { r.FileName = f.FullName; list.Add(r); }
            }
            catch { }
        }
        return list.OrderByDescending(r => r.Date).ToList();
    }

    public static void Delete(UninstallRecord r)
    {
        try { File.Delete(r.FileName); } catch { }
    }

    /// <summary>
    /// Deshace la limpieza de restos: reimporta el registro (claves, servicios y reglas del firewall),
    /// vuelve a crear las tareas programadas y saca los archivos de la papelera. Devuelve un resumen.
    /// </summary>
    public static (int ok, int failed, string message) Restore(UninstallRecord r)
    {
        int ok = 0, failed = 0;
        foreach (var reg in r.RegFiles)
        {
            if (File.Exists(reg) && Util.Run("reg.exe", $"import \"{reg}\"", 120_000).exitCode == 0) ok++;
            else failed++;
        }
        foreach (var t in r.Tasks.Where(t => t.Length == 2))
        {
            var slash = t[0].LastIndexOf('\\');
            var (code, _) = Util.PowerShell(
                $"Register-ScheduledTask -Xml (Get-Content -Raw '{t[1].Replace("'", "''")}') -TaskPath '{t[0][..(slash + 1)].Replace("'", "''")}' " +
                $"-TaskName '{t[0][(slash + 1)..].Replace("'", "''")}' -Force | Out-Null");
            if (code == 0) ok++; else failed++;
        }
        // Solo lo que se borró en esta desinstalación (o después), no un borrado anterior con la misma ruta
        var restored = RecycleBin.Restore(r.Recycled, r.Date.AddMinutes(-1));
        ok += restored.Count;
        var missing = r.Recycled.Count - restored.Count;
        failed += missing;
        r.Restored = ok > 0;   // si no volvió nada, se puede volver a intentar
        Save(r);
        Scheduler.Log($"Deshacer desinstalación de {r.Program}: {ok} restaurados, {failed} fallos");
        var msg = $"{ok} {Loc.T("elementos restaurados")}"
                  + (missing > 0 ? $"\n{missing} {Loc.T("archivos ya no estaban en la Papelera de reciclaje (puede que se vaciara).")}" : "")
                  + (r.OtherItems > 0 ? "\n" + Loc.T("Los servicios restaurados funcionarán después de reiniciar el equipo.") : "");
        return (ok, failed, msg);
    }
}
