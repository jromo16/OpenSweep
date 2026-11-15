using Microsoft.Win32;
using OpenSweep.Core;

namespace OpenSweep.Services;

/// <summary>
/// Limpiezas profundas de Windows que deben hacerse con las herramientas del propio sistema
/// (los archivos pertenecen a TrustedInstaller y borrarlos a mano puede dejar Windows a medias):
/// el Liberador de espacio (cleanmgr) y DISM.
/// </summary>
public static class SystemCleanup
{
    private const string VolumeCaches = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VolumeCaches";
    private const int SageSet = 77;   // número de perfil propio de OpenSweep para cleanmgr

    public static string[] PreviousInstallationDirs =>
    [
        Path.Combine(Path.GetPathRoot(Util.WinDir) ?? @"C:\", "Windows.old"),
        Path.Combine(Path.GetPathRoot(Util.WinDir) ?? @"C:\", "$Windows.~BT"),
        Path.Combine(Path.GetPathRoot(Util.WinDir) ?? @"C:\", "$Windows.~WS"),
    ];

    public static bool HasPreviousInstallation => PreviousInstallationDirs.Any(Directory.Exists);

    public static long PreviousInstallationBytes() => PreviousInstallationDirs.Where(Directory.Exists).Sum(Util.DirSize);

    /// <summary>
    /// Ejecuta los módulos indicados del Liberador de espacio de Windows de forma desatendida.
    /// Marca cada módulo con StateFlags0077, ejecuta «cleanmgr /sagerun:77» y quita las marcas.
    /// </summary>
    public static bool RunCleanmgr(params string[] handlers)
    {
        var flag = $"StateFlags{SageSet:0000}";
        var marked = new List<string>();
        try
        {
            using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey(VolumeCaches, true);
            if (root == null) return false;
            foreach (var h in handlers)
            {
                using var k = root.OpenSubKey(h, true);
                if (k == null) continue;   // módulo no disponible en esta versión de Windows
                k.SetValue(flag, 2, RegistryValueKind.DWord);
                marked.Add(h);
            }
            if (marked.Count == 0) return false;
            var (code, _) = Util.Run("cleanmgr.exe", $"/sagerun:{SageSet}", 60 * 60_000);
            Scheduler.Log($"Liberador de espacio ({string.Join(", ", marked)}): código {code}");
            return code == 0;
        }
        catch { return false; }
        finally
        {
            // Quitar las marcas para no alterar la configuración del Liberador de espacio del usuario
            try
            {
                using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey(VolumeCaches, true);
                foreach (var h in marked)
                {
                    using var k = root?.OpenSubKey(h, true);
                    k?.DeleteValue(flag, false);
                }
            }
            catch { }
        }
    }

    /// <summary>Elimina los componentes de Windows Update reemplazados por versiones nuevas (WinSxS). Puede tardar minutos.</summary>
    public static bool ComponentCleanup()
    {
        var (code, output) = Util.Run("dism.exe", "/Online /Cleanup-Image /StartComponentCleanup /Quiet", 90 * 60_000);
        Scheduler.Log($"DISM StartComponentCleanup: código {code}");
        return code == 0 || output.Contains("successfully", StringComparison.OrdinalIgnoreCase);
    }
}
