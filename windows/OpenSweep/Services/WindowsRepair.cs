using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using OpenSweep.Core;

namespace OpenSweep.Services;

/// <summary>
/// «Reparar Windows»: DISM /RestoreHealth (repara el almacén de componentes, descargando de Windows Update)
/// y después sfc /scannow (repara los archivos del sistema con ese almacén). Es el orden que recomienda Microsoft.
/// Sigue en marcha aunque el usuario cambie de sección; la vista se vuelve a conectar con <see cref="Changed"/>.
/// </summary>
public static partial class WindowsRepair
{
    public static bool Running { get; private set; }
    public static int Step { get; private set; }            // 1 = DISM, 2 = SFC
    public static double Percent { get; private set; }
    public static string? Result { get; private set; }
    public static bool? Success { get; private set; }

    /// <summary>Se dispara (en un hilo de fondo) cuando cambia el progreso o termina.</summary>
    public static event Action? Changed;

    public static string StepText => Step switch
    {
        1 => Loc.T("Paso 1 de 2: reparando la imagen de Windows (DISM)"),
        2 => Loc.T("Paso 2 de 2: comprobando los archivos del sistema (SFC)"),
        _ => "",
    };

    /// <summary>Extrae el último porcentaje de un fragmento de salida («[==== 45.3% ====]» o «Verificación 45% completada»).</summary>
    public static double? LastPercent(string text)
    {
        double? last = null;
        foreach (Match m in PercentRegex().Matches(text))
            if (double.TryParse(m.Groups[1].Value.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) && v <= 100)
                last = v;
        return last;
    }

    public static async Task RunAsync()
    {
        if (Running) return;
        Running = true; Result = null; Success = null; Percent = 0;
        try
        {
            Step = 1; Changed?.Invoke();
            var (dismCode, _) = await RunStreaming("dism.exe", "/Online /Cleanup-Image /RestoreHealth", Encoding.Default);
            Scheduler.Log($"Reparar Windows: DISM RestoreHealth, código {dismCode}");

            Step = 2; Percent = 0; Changed?.Invoke();
            // sfc escribe en UTF-16 cuando su salida se redirige
            var (sfcCode, sfcOut) = await RunStreaming("sfc.exe", "/scannow", Encoding.Unicode);
            Scheduler.Log($"Reparar Windows: SFC, código {sfcCode}");

            // El mensaje final de sfc ya viene en el idioma de Windows y explica el resultado
            var lines = sfcOut.Replace("\0", "").Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                              .Where(l => l.Length > 0 && LastPercent(l) == null).ToList();
            var summary = string.Join(" ", lines.SkipWhile(l => !l.Contains('.')).TakeLast(3));
            Success = dismCode == 0 && sfcCode == 0;
            Result = (dismCode == 0 ? Loc.T("La imagen de Windows está bien o se reparó.") : $"{Loc.T("DISM no pudo terminar la reparación (código")} {dismCode}). ")
                     + (summary.Length > 0 ? " " + summary : "");
        }
        catch (Exception ex)
        {
            Success = false;
            Result = ex.Message;
        }
        finally
        {
            Running = false; Step = 0;
            Changed?.Invoke();
        }
    }

    private static async Task<(int code, string output)> RunStreaming(string file, string args, Encoding encoding)
    {
        var psi = new ProcessStartInfo(file, args)
        {
            RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true, StandardOutputEncoding = encoding,
        };
        using var p = Process.Start(psi)!;
        var all = new StringBuilder();
        var buffer = new char[512];
        int n;
        while ((n = await p.StandardOutput.ReadAsync(buffer, 0, buffer.Length)) > 0)
        {
            var chunk = new string(buffer, 0, n);
            all.Append(chunk);
            if (LastPercent(chunk.Replace("\0", "")) is { } pct && Math.Abs(pct - Percent) >= 0.5)
            {
                Percent = pct;
                Changed?.Invoke();
            }
        }
        await p.WaitForExitAsync();
        return (p.ExitCode, all.ToString());
    }

    [GeneratedRegex(@"(\d{1,3}(?:[.,]\d)?)\s?%")]
    private static partial Regex PercentRegex();
}
