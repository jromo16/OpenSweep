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
