using OpenSweep.Core;
using System.Windows;
using System.Windows.Threading;

namespace OpenSweep;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnUnhandled;
        base.OnStartup(e);
        var li = Array.IndexOf(e.Args, "--lang");
        Core.Loc.Init(li >= 0 && li + 1 < e.Args.Length ? e.Args[li + 1] : null);
        var ti = Array.IndexOf(e.Args, "--theme");
        Core.ThemeManager.Apply(ti >= 0 && ti + 1 < e.Args.Length ? e.Args[ti + 1] : Core.AppSettings.Current.Theme);

        if (e.Args.Contains("--auto-clean"))
        {
            // Limpieza programada: sin ventana
            Services.Scheduler.RunAutoClean();
            Shutdown();
            return;
        }
        if (e.Args.Contains("--tray"))
        {
            _tray = new TrayHost();
            return;
        }
        var main = new MainWindow();
        main.Closed += (_, _) => { if (_tray == null) Shutdown(); };
        main.Show();
    }

    private TrayHost? _tray;

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        base.OnExit(e);
    }

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(Loc.T("Se produjo un error inesperado:\n\n") + e.Exception.Message, "OpenSweep", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
