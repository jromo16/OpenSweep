using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using OpenSweep.Core;

namespace OpenSweep.Services;

/// <summary>
/// Monitor de instalaciones automático (como «Programas rastreados» de Revo), lo ejecuta el monitor de la bandeja:
/// 1. Guarda una instantánea del sistema en segundo plano.
/// 2. Vigila los procesos que parecen instaladores para saber cuándo empezó una instalación.
/// 3. Cuando aparece un programa nuevo en «Programas instalados» (y el instalador ya terminó), toma otra instantánea,
///    compara y guarda solo lo creado desde que empezó la instalación. Ese registro se usa luego al desinstalar.
/// </summary>
public sealed class AutoTrace
{
    private Snapshot? _baseline;
    private DateTime _baselineTime;
    private HashSet<string>? _known;
    private readonly Dictionary<int, (string name, DateTime start)> _installers = [];
    private DateTime? _pendingSince;
    private bool _busy;

    public bool Ready => _baseline != null;

    /// <summary>¿El nombre del proceso es de un instalador? (setup.exe, instalador de Inno/NSIS…, no desinstaladores)</summary>
    public static bool LooksLikeInstaller(string processName)
    {
        var n = processName.ToLowerInvariant();
        if (n.Contains("unins") || n.Contains("uninst") || n.StartsWith("opensweep")) return false;
        return n.Contains("setup") || n.Contains("install") || n.Contains("instalador") || n.EndsWith("_x64") && n.Contains("redist");
    }

    /// <summary>Primera instantánea (tarda alrededor de un minuto, en segundo plano).</summary>
    public void TakeBaseline()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            _known = UninstallIds();
            _baseline = InstallMonitor.Take(new Progress<string>());
            _baselineTime = DateTime.Now;
        }
        finally { _busy = false; }
    }

    /// <summary>Anota los instaladores que se están ejecutando (cada pocos segundos).</summary>
    public void PollProcesses()
    {
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (_installers.ContainsKey(p.Id) || !LooksLikeInstaller(p.ProcessName)) continue;
                _installers[p.Id] = (p.ProcessName, p.StartTime);
            }
            catch { }
            finally { p.Dispose(); }
        }
    }

    private bool InstallerRunning()
    {
        foreach (var pid in _installers.Keys.ToList())
        {
            try { using var p = Process.GetProcessById(pid); if (!p.HasExited) return true; }
            catch { }
        }
        return false;
    }

    private static HashSet<string> UninstallIds() =>
        UninstallerService.GetInstalledPrograms(includeSystem: true).Select(p => p.Id).ToHashSet();

    /// <summary>
    /// Comprueba si se instaló algo (cada 30 s). Devuelve el registro guardado, o null si no hubo instalación.
    /// </summary>
    public InstallTrace? Check()
    {
        if (_busy || _baseline == null || _known == null) return null;
        _busy = true;
        try
        {
            var now = UninstallIds();
            var added = now.Except(_known).ToList();
            if (added.Count == 0)
            {
                _known = now;   // también recoge las desinstalaciones
                // Renovar la instantánea cada 6 horas si no hay nada instalándose
                if ((DateTime.Now - _baselineTime).TotalHours > 6 && !InstallerRunning())
                {
                    _installers.Clear();
                    _baseline = InstallMonitor.Take(new Progress<string>());
                    _baselineTime = DateTime.Now;
                }
                return null;
            }

            // Esperar a que termine el instalador (como mucho 15 minutos) para no cortar la instalación a medias
            _pendingSince ??= DateTime.Now;
            if (InstallerRunning() && (DateTime.Now - _pendingSince.Value).TotalMinutes < 15) return null;

            var after = InstallMonitor.Take(new Progress<string>());
            var since = _installers.Values.Where(i => i.start >= _baselineTime).Select(i => i.start).DefaultIfEmpty(_baselineTime).Min();
            var installer = _installers.Values.OrderBy(i => i.start).Select(i => i.name).FirstOrDefault() ?? Loc.T("instalación detectada");
            var trace = InstallMonitor.Diff(_baseline, after, installer + ".exe");
            KeepSince(trace, since.AddMinutes(-1));

            var program = UninstallerService.GetInstalledPrograms(includeSystem: true).FirstOrDefault(p => added.Contains(p.Id));
            if (program != null)
            {
                trace.Name = program.Name;
                trace.UninstallHive = program.Hive == RegistryHive.CurrentUser ? "HKCU" : "HKLM";
                trace.UninstallView = program.View == RegistryView.Registry32 ? "32" : "64";
                trace.UninstallKey = program.KeyPath;
            }
            trace.Automatic = true;
            if (trace.Items.Count > 0) InstallMonitor.Save(trace);

            _baseline = after;
            _baselineTime = DateTime.Now;
            _known = now;
            _installers.Clear();
            _pendingSince = null;
            Scheduler.Log($"Instalación registrada automáticamente: {trace.Name} ({trace.Items.Count} elementos)");
            return trace.Items.Count > 0 ? trace : null;
        }
        catch { return null; }
        finally { _busy = false; }
    }

    /// <summary>
    /// Deja solo lo creado o modificado desde que empezó la instalación: así no se atribuye al programa
    /// algo que creó otra aplicación desde la última instantánea.
    /// </summary>
    internal static void KeepSince(InstallTrace trace, DateTime since)
    {
        trace.Items.RemoveAll(i =>
        {
            try
            {
                return i.Kind switch
                {
                    "dir" => Directory.Exists(i.Path) && Directory.GetCreationTime(i.Path) < since,
                    "file" => File.Exists(i.Path) && File.GetCreationTime(i.Path) < since,
                    "key" or "value" => i.Path.Contains(@"\CurrentVersion\Uninstall\", StringComparison.OrdinalIgnoreCase)
                        ? false
                        : KeyLastWrite(i.Hive, i.View, i.Path) is { } t && t < since,
                    _ => false,
                };
            }
            catch { return false; }
        });
    }

    [DllImport("advapi32.dll")]
    private static extern int RegQueryInfoKey(SafeRegistryHandle hKey, IntPtr lpClass, IntPtr lpcchClass, IntPtr lpReserved,
        IntPtr lpcSubKeys, IntPtr lpcbMaxSubKeyLen, IntPtr lpcbMaxClassLen, IntPtr lpcValues, IntPtr lpcbMaxValueNameLen,
        IntPtr lpcbMaxValueLen, IntPtr lpcbSecurityDescriptor, out long lpftLastWriteTime);

    /// <summary>Fecha de la última modificación de una clave del registro.</summary>
    internal static DateTime? KeyLastWrite(string hive, string view, string path)
    {
        using var k = RegistryKey.OpenBaseKey(hive == "HKCU" ? RegistryHive.CurrentUser : RegistryHive.LocalMachine,
            view == "32" ? RegistryView.Registry32 : RegistryView.Registry64).OpenSubKey(path);
        if (k == null) return null;
        return RegQueryInfoKey(k.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
            IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, out var ft) == 0 ? DateTime.FromFileTime(ft) : null;
    }
}
