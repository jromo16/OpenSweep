using System.Windows;
using System.Windows.Controls;
using OpenSweep.Core;
using OpenSweep.Services;

namespace OpenSweep.Views;

public partial class LeftoversWindow : Window
{
    private readonly InstalledProgram _program;
    private List<Leftover> _items = [];
    private readonly bool _preset;

    /// <summary>Muestra una lista ya conocida (p. ej. del monitor de instalaciones) sin buscar.</summary>
    public LeftoversWindow(string title, List<Leftover> items) : this(UninstallerService.FakeProgram(title, null), true)
    {
        _preset = true;
        _items = items;
        TitleText.Text = title;
        InfoText.Text = Loc.T("Elementos creados por la instalación que todavía existen. Revisa la lista antes de eliminar.");
        ScanBtn.Visibility = ModeSafe.Visibility = ModeModerate.Visibility = ModeAdvanced.Visibility = Visibility.Collapsed;
    }

    public LeftoversWindow(InstalledProgram program, bool forced)
    {
        InitializeComponent();
        _program = program;
        TitleText.Text = (forced ? Loc.T("Desinstalación forzada: ") : Loc.T("Restos de ")) + program.Name;
        InfoText.Text = forced
            ? Loc.T("Se buscarán los archivos, carpetas y claves del registro del programa. Revisa la lista antes de eliminar.")
            : Loc.T("El desinstalador terminó. Busca ahora los archivos, carpetas y claves del registro que hayan quedado.");
        Loaded += (_, _) =>
        {
            if (!_preset) { Scan_Click(this, new RoutedEventArgs()); return; }
            Grid.ItemsSource = _items;
            DeleteBtn.IsEnabled = _items.Count > 0;
            StatusText.Text = $"{_items.Count} {Loc.T("elementos")} · {Util.FormatBytes(_items.Sum(i => i.Size))}";
        };
    }

    private ScanMode Mode => ModeSafe.IsChecked == true ? ScanMode.Seguro : ModeAdvanced.IsChecked == true ? ScanMode.Avanzado : ScanMode.Moderado;

    private async void Scan_Click(object sender, RoutedEventArgs e)
    {
        ScanBtn.IsEnabled = DeleteBtn.IsEnabled = false;
        Progress.Visibility = Visibility.Visible;
        StatusText.Text = Loc.T("Buscando...");
        var mode = Mode;
        _items = await Task.Run(() => UninstallerService.FindLeftovers(_program, mode, CancellationToken.None));
        _items = _items.OrderBy(i => i.IsRegistry).ThenBy(i => i.Display).ToList();
        Grid.ItemsSource = _items;
        Progress.Visibility = Visibility.Collapsed;
        ScanBtn.IsEnabled = true;
        DeleteBtn.IsEnabled = _items.Count > 0;
        StatusText.Text = _items.Count == 0
            ? Loc.T("No se encontraron restos. ¡El programa se eliminó por completo!")
            : $"{_items.Count} {Loc.T("elementos encontrados")} · {Util.FormatBytes(_items.Sum(i => i.Size))}";
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        Grid.CommitEdit(DataGridEditingUnit.Row, true);
        var sel = _items.Where(i => i.Selected).ToList();
        if (sel.Count == 0) return;
        if (Dialogs.Show($"{Loc.T("Se eliminarán")} {sel.Count} {Loc.T("elementos de forma permanente.")}\n{Loc.T("Las claves del registro se guardan antes en una copia de seguridad.")}\n\n{Loc.T("¿Continuar?")}",
                Loc.T("Eliminar restos"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        DeleteBtn.IsEnabled = ScanBtn.IsEnabled = false;
        Progress.Visibility = Visibility.Visible;
        await Task.Run(() => UninstallerService.BackupRegistryLeftovers(sel));
        var removed = new List<Leftover>();
        int ok = await Dialogs.RunAsync(Loc.T("Eliminando restos…"), sel,
            i =>
            {
                if (!UninstallerService.DeleteLeftover(i)) return false;
                lock (removed) removed.Add(i);
                return true;
            },
            i => System.IO.Path.GetFileName(i.Path),
            (done, bad) => $"{done} {Loc.T("restos eliminados")}" + (bad > 0 ? $" · {bad} {Loc.T("no se pudieron eliminar")}" : ""));
        int failed = sel.Count - ok;
        Progress.Visibility = Visibility.Collapsed;
        _items = _items.Except(removed).ToList();
        Grid.ItemsSource = _items;
        ScanBtn.IsEnabled = true;
        DeleteBtn.IsEnabled = _items.Count > 0;
        StatusText.Text = $"{Loc.T("Eliminados")}: {ok}" + (failed > 0 ? $" · {Loc.T("No se pudieron eliminar")}: {failed} (pueden estar en uso; reinicia y vuelve a intentarlo)" : "");
    }

    private void CheckAll_Click(object sender, RoutedEventArgs e) { _items.ForEach(i => i.Selected = true); Grid.Items.Refresh(); }
    private void UncheckAll_Click(object sender, RoutedEventArgs e) { _items.ForEach(i => i.Selected = false); Grid.Items.Refresh(); }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
