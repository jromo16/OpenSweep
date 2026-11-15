using System.Text.Json;
using OpenSweep.Core;

namespace OpenSweep.Services;

public enum DiskState { Good, Warning, Bad, Unknown }

/// <summary>Estado de un disco físico según lo que informa el propio disco (S.M.A.R.T. / NVMe).</summary>
public sealed class DiskHealthInfo
{
    public string Name { get; init; } = "";
    public string Media { get; init; } = "";        // SSD, HDD, Unspecified
    public string Bus { get; init; } = "";          // NVMe, SATA, USB…
    public long Size { get; init; }
    public string Health { get; init; } = "";       // Healthy, Warning, Unhealthy
    public int? Temperature { get; init; }
    public int? Wear { get; init; }                 // % de vida consumida (SSD)
    public long? ReadErrors { get; init; }          // errores de lectura sin corregir
    public long? WriteErrors { get; init; }
    public long? PowerOnHours { get; init; }

    public DiskState State => DiskHealth.Evaluate(this).state;
    public string Summary => DiskHealth.Evaluate(this).text;
    public string Icon => State switch { DiskState.Good => "", DiskState.Warning => "", DiskState.Bad => "", _ => "" };
    public string Color => State switch { DiskState.Good => "#10B981", DiskState.Warning => "#FF9F0A", DiskState.Bad => "#FF3B30", _ => "#8E8E93" };
    public string Title => $"{Name} · {(Media is "SSD" or "HDD" ? Media : Bus)} · {Util.FormatBytes(Size)}";

    public string Details
    {
        get
        {
            var parts = new List<string>();
            if (Temperature is > 0) parts.Add($"{Temperature} °C");
            if (Wear != null) parts.Add($"{Loc.T("vida útil restante")} {Math.Max(0, 100 - Wear.Value)} %");
            if (PowerOnHours is > 0) parts.Add($"{PowerOnHours:N0} {Loc.T("horas encendido")}");
            return string.Join(" · ", parts);
        }
    }
}

public static class DiskHealth
{
    /// <summary>Lee los discos físicos con Get-PhysicalDisk y sus contadores de fiabilidad (requiere administrador).</summary>
    public static List<DiskHealthInfo> Read()
    {
        const string script =
            "@(Get-PhysicalDisk | ForEach-Object { $r = $_ | Get-StorageReliabilityCounter -ErrorAction SilentlyContinue; " +
            "[pscustomobject]@{ Name=$_.FriendlyName; Media=[string]$_.MediaType; Bus=[string]$_.BusType; Size=[int64]$_.Size; Health=[string]$_.HealthStatus; " +
            "Temperature=$r.Temperature; Wear=$r.Wear; ReadErrors=$r.ReadErrorsUncorrected; WriteErrors=$r.WriteErrorsUncorrected; PowerOnHours=$r.PowerOnHours } }) | ConvertTo-Json -Compress";
        var (code, output) = Util.PowerShell(script, 60_000);
        if (code != 0) return [];
        return Parse(output);
    }

    public static List<DiskHealthInfo> Parse(string json)
    {
        try
        {
            json = json.Trim();
            if (json.Length == 0) return [];
            if (json.StartsWith('{')) json = $"[{json}]";
            return JsonSerializer.Deserialize<List<DiskHealthInfo>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })?
                .Where(d => d.Size > 0).ToList() ?? [];
        }
        catch { return []; }
    }

    /// <summary>Traduce los datos del disco a un estado y una frase sencilla.</summary>
    public static (DiskState state, string text) Evaluate(DiskHealthInfo d)
    {
        if (d.Health.Equals("Unhealthy", StringComparison.OrdinalIgnoreCase))
            return (DiskState.Bad, Loc.T("El disco está fallando. Copia tus archivos importantes cuanto antes."));
        if (d.ReadErrors is > 0 || d.WriteErrors is > 0)
            return (DiskState.Warning, Loc.T("El disco tiene errores que no pudo corregir. Haz una copia de seguridad."));
        if (d.Wear is >= 90)
            return (DiskState.Warning, Loc.T("Al SSD le queda poca vida útil. Piensa en reemplazarlo."));
        if (d.Health.Equals("Warning", StringComparison.OrdinalIgnoreCase))
            return (DiskState.Warning, Loc.T("Windows detecta un problema en el disco. Haz una copia de seguridad."));
        if (d.Temperature is >= 65)
            return (DiskState.Warning, Loc.T("El disco está muy caliente. Revisa la ventilación del equipo."));
        if (d.Health.Equals("Healthy", StringComparison.OrdinalIgnoreCase))
            return (DiskState.Good, Loc.T("En buen estado"));
        return (DiskState.Unknown, Loc.T("Este disco no informa de su estado"));
    }
}
