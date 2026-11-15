using System.Text.Json;
using Microsoft.Win32;
using OpenSweep.Core;

namespace OpenSweep.Services;

/// <summary>Un valor del registro que forma parte de un ajuste de privacidad.</summary>
public sealed record PrivacyValue(RegistryHive Hive, string Path, string Name, int PrivateValue);

public sealed class PrivacySetting
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required PrivacyValue[] Values { get; init; }
    /// <summary>Se recomienda activarlo (sin efectos secundarios conocidos).</summary>
    public bool Recommended { get; init; } = true;
    public bool Enabled { get; set; }   // true = protegido (valores privados aplicados)

    public string TitleText => Loc.T(Title);
    public string DescriptionText => Loc.T(Description);
}

/// <summary>
/// Ajustes de privacidad de Windows, al estilo de O&amp;O ShutUp10, pero solo los seguros:
/// valores del registro reversibles, sin desactivar servicios ni actualizaciones.
/// Antes de cambiar un valor se guarda el original en privacidad.json para restaurarlo exactamente.
/// </summary>
public static class PrivacyService
{
    private const string Cdm = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";
    private static string BackupFile => Path.Combine(AppSettings.Folder, "privacidad.json");

    public static List<PrivacySetting> Settings() =>
    [
        new()
        {
            Id = "adid", Title = "ID de publicidad",
            Description = "Las apps no podrán usar tu ID de publicidad para mostrarte anuncios personalizados.",
            Values = [new(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", 0)],
        },
        new()
        {
            Id = "tailored", Title = "Experiencias personalizadas",
            Description = "Microsoft no usará tus datos de diagnóstico para mostrarte sugerencias y anuncios.",
            Values = [new(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Privacy", "TailoredExperiencesWithDiagnosticDataEnabled", 0)],
        },
        new()
        {
            Id = "telemetry", Title = "Datos de diagnóstico al mínimo",
            Description = "Envía a Microsoft solo los datos de diagnóstico obligatorios.",
            Values = [new(RegistryHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", 1)],
        },
        new()
        {
            Id = "startads", Title = "Sugerencias y anuncios en Inicio",
            Description = "Quita las apps sugeridas del menú Inicio y evita que Windows instale apps promocionadas.",
            Values =
            [
                new(RegistryHive.CurrentUser, Cdm, "SystemPaneSuggestionsEnabled", 0),
                new(RegistryHive.CurrentUser, Cdm, "SubscribedContent-338388Enabled", 0),
                new(RegistryHive.CurrentUser, Cdm, "SilentInstalledAppsEnabled", 0),
            ],
        },
        new()
        {
            Id = "tips", Title = "Consejos y trucos de Windows",
            Description = "Desactiva las notificaciones de consejos y las sugerencias en Configuración.",
            Values =
            [
                new(RegistryHive.CurrentUser, Cdm, "SubscribedContent-338389Enabled", 0),
                new(RegistryHive.CurrentUser, Cdm, "SubscribedContent-338393Enabled", 0),
                new(RegistryHive.CurrentUser, Cdm, "SubscribedContent-353694Enabled", 0),
                new(RegistryHive.CurrentUser, Cdm, "SubscribedContent-353696Enabled", 0),
            ],
        },
        new()
        {
            Id = "lockscreen", Title = "Publicidad en la pantalla de bloqueo",
            Description = "Quita los datos curiosos, consejos y anuncios de la pantalla de bloqueo.",
            Values =
            [
                new(RegistryHive.CurrentUser, Cdm, "RotatingLockScreenOverlayEnabled", 0),
                new(RegistryHive.CurrentUser, Cdm, "SubscribedContent-338387Enabled", 0),
            ],
        },
        new()
        {
            Id = "websearch", Title = "Resultados de Bing en la búsqueda",
            Description = "La búsqueda del menú Inicio solo muestra resultados de tu PC (se aplica al reiniciar sesión).",
            Values = [new(RegistryHive.CurrentUser, @"Software\Policies\Microsoft\Windows\Explorer", "DisableSearchBoxSuggestions", 1)],
        },
        new()
        {
            Id = "activity", Title = "Historial de actividad",
            Description = "Windows no guardará ni enviará el historial de las apps y archivos que usas.",
            Values =
            [
                new(RegistryHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\System", "PublishUserActivities", 0),
                new(RegistryHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\System", "UploadUserActivities", 0),
            ],
        },
        new()
        {
            Id = "recall", Title = "Recall (capturas de tu actividad)",
            Description = "Impide que Windows guarde capturas periódicas de tu pantalla para la función Recall.",
            Values = [new(RegistryHive.CurrentUser, @"Software\Policies\Microsoft\Windows\WindowsAI", "DisableAIDataAnalysis", 1)],
        },
        new()
        {
            Id = "feedback", Title = "Solicitudes de opinión",
            Description = "Windows no te pedirá que valores tu experiencia.",
            Values = [new(RegistryHive.CurrentUser, @"Software\Microsoft\Siuf\Rules", "NumberOfSIUFInPeriod", 0)],
        },
        new()
        {
            Id = "inking", Title = "Personalización de escritura y voz",
            Description = "No se recopila lo que escribes a mano ni tu forma de escribir para mejorar el reconocimiento.",
            Values =
            [
                new(RegistryHive.CurrentUser, @"Software\Microsoft\InputPersonalization", "RestrictImplicitInkCollection", 1),
                new(RegistryHive.CurrentUser, @"Software\Microsoft\InputPersonalization", "RestrictImplicitTextCollection", 1),
            ],
        },
        new()
        {
            Id = "copilot", Title = "Copilot en Windows", Recommended = false,
            Description = "Desactiva el asistente Copilot integrado en Windows. Déjalo activado si lo usas.",
            Values = [new(RegistryHive.CurrentUser, @"Software\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot", 1)],
        },
    ];

    private static int? Read(PrivacyValue v)
    {
        try
        {
            using var k = RegistryKey.OpenBaseKey(v.Hive, RegistryView.Registry64).OpenSubKey(v.Path);
            return k?.GetValue(v.Name) is int i ? i : null;
        }
        catch { return null; }
    }

    /// <summary>Lee el estado actual de cada ajuste (protegido = todos sus valores son los privados).</summary>
    public static List<PrivacySetting> Load()
    {
        var list = Settings();
        foreach (var s in list) s.Enabled = s.Values.All(v => Read(v) == v.PrivateValue);
        return list;
    }

    // ---- Copia de los valores originales: "HKCU|ruta|nombre" -> valor (null = no existía) ----
    private static Dictionary<string, int?> LoadBackup()
    {
        if (!File.Exists(BackupFile)) return [];
        try { return JsonSerializer.Deserialize<Dictionary<string, int?>>(File.ReadAllText(BackupFile)) ?? []; }
        catch
        {
            // Copia dañada: se aparta (no se sobrescribe) para no perder los valores originales
            try { File.Move(BackupFile, BackupFile + $".dañado-{DateTime.Now:yyyyMMddHHmmss}"); } catch { }
            return [];
        }
    }

    private static void SaveBackup(Dictionary<string, int?> b)
    {
        Directory.CreateDirectory(AppSettings.Folder);
        File.WriteAllText(BackupFile, JsonSerializer.Serialize(b, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string Key(PrivacyValue v) => $"{(v.Hive == RegistryHive.CurrentUser ? "HKCU" : "HKLM")}|{v.Path}|{v.Name}";

    /// <summary>Aplica (protege) o deshace un ajuste. Al deshacer se restaura el valor que había antes.</summary>
    public static bool Set(PrivacySetting s, bool enable)
    {
        var backup = LoadBackup();
        try
        {
            if (enable)
            {
                // Solo se cambian valores numéricos (DWORD): así siempre se puede restaurar el original exacto
                foreach (var v in s.Values)
                {
                    using var rk = RegistryKey.OpenBaseKey(v.Hive, RegistryView.Registry64).OpenSubKey(v.Path);
                    if (rk?.GetValue(v.Name) is { } current && current is not int) return false;
                    if (!backup.ContainsKey(Key(v))) backup[Key(v)] = Read(v);   // guardar el original una sola vez
                }
                SaveBackup(backup);   // la copia se guarda ANTES de tocar el registro
            }
            foreach (var v in s.Values)
            {
                using var root = RegistryKey.OpenBaseKey(v.Hive, RegistryView.Registry64);
                if (enable)
                {
                    using var k = root.CreateSubKey(v.Path, true);
                    k.SetValue(v.Name, v.PrivateValue, RegistryValueKind.DWord);
                }
                else
                {
                    using var k = root.OpenSubKey(v.Path, true);
                    if (k == null) continue;
                    if (backup.TryGetValue(Key(v), out var original) && original != null) k.SetValue(v.Name, original.Value, RegistryValueKind.DWord);
                    else k.DeleteValue(v.Name, false);   // no existía: valor por defecto de Windows
                    backup.Remove(Key(v));
                }
            }
            SaveBackup(backup);
            s.Enabled = enable;
            Scheduler.Log($"Privacidad: «{s.Title}» {(enable ? "activado" : "restaurado")}");
            return true;
        }
        catch
        {
            try { SaveBackup(backup); } catch { }
            return false;
        }
    }
}
