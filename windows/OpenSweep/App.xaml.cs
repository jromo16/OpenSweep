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

        var st = Array.IndexOf(e.Args, "--selftest");
        if (st >= 0)
        {
            // Prueba completa (solo en Windows Sandbox). Fuera del hilo de la interfaz para no bloquear los await.
            var report = st + 1 < e.Args.Length && !e.Args[st + 1].StartsWith("--") ? e.Args[st + 1] : Path.Combine(AppContext.BaseDirectory, "informe-prueba.txt");
            var failures = Task.Run(() => Services.SelfTest.RunAsync(report, e.Args.Contains("--full"))).GetAwaiter().GetResult();
            Shutdown(failures);
            return;
        }
        if (Services.CommandLine.IsCommand(e.Args))
        {
            // Línea de comandos: limpiar o analizar sin ventana
            Shutdown(Services.CommandLine.Run(e.Args));
            return;
        }
        var shred = Array.IndexOf(e.Args, "--shred");
        if (shred >= 0 && shred + 1 < e.Args.Length)
        {
            // «Destruir con OpenSweep» desde el menú del clic derecho: solo los diálogos, sin la ventana principal
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Dispatcher.BeginInvoke(async () => { await Views.ShredCommand.RunAsync(e.Args[shred + 1]); Shutdown(); });
            return;
        }
        if (e.Args.Contains("--auto-clean"))
        {
            // Limpieza programada: sin ventana
            Services.Scheduler.RunAutoClean();
            Shutdown();
            return;
        }
        if (e.Args.Contains("--tray"))
        {
            // Un solo monitor en la bandeja aunque se lance varias veces
            _trayMutex = new Mutex(true, @"Local\OpenSweepTray", out bool first);
            if (!first) { Shutdown(); return; }
            _tray = new TrayHost();
            return;
        }
        var main = new MainWindow();
        main.Closed += (_, _) => { if (_tray == null) Shutdown(); };
        main.Show();
        // «Desinstalar con OpenSweep» desde el menú del clic derecho
        var un = Array.IndexOf(e.Args, "--uninstall");
        if (un >= 0 && un + 1 < e.Args.Length)
        {
            var path = e.Args[un + 1];
            main.ContentRendered += async (_, _) =>
            {
                if (main.Open<Views.UninstallerView>() is { } view) await view.UninstallFromPath(path);
            };
        }
    }

    private TrayHost? _tray;
    private Mutex? _trayMutex;

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        base.OnExit(e);
    }

    /// <summary>Marca o desmarca la casilla de una tabla con un solo clic.</summary>
    private void GridCell_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not System.Windows.Controls.DataGridCell { IsReadOnly: false, Column: System.Windows.Controls.DataGridCheckBoxColumn } cell) return;
        if (cell.Content is not System.Windows.Controls.CheckBox box) return;
        // Se cambia directamente el valor enlazado (UpdateSourceTrigger=PropertyChanged lo guarda en el momento)
        var binding = System.Windows.Data.BindingOperations.GetBindingExpression(box, System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty);
        box.SetCurrentValue(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, !(box.IsChecked ?? false));
        binding?.UpdateSource();
        e.Handled = true;
    }

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(Loc.T("Se produjo un error inesperado:\n\n") + e.Exception.Message, "OpenSweep", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
