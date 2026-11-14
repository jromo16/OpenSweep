using System.Windows;
using System.Windows.Controls;
using OpenSweep.Core;
using OpenSweep.Services;

namespace OpenSweep.Views;

public partial class LeftoversWindow : Window
{
    private readonly InstalledProgram _program;
    private List<Leftover> _items = [];

    public LeftoversWindow(InstalledProgram program, bool forced)
    {
        InitializeComponent();
        _program = program;
        TitleText.Text = (forced ? "Desinstalación forzada: " : "Restos de ") + program.Name;
        InfoText.Text = forced
            ? "Se buscarán los archivos, carpetas y claves del registro del programa. Revisa la lista antes de eliminar."
            : "El desinstalador terminó. Busca ahora los archivos, carpetas y claves del registro que hayan quedado.";
        Loaded += (_, _) => Scan_Click(this, new RoutedEventArgs());
    }

    private ScanMode Mode => ModeSafe.IsChecked == true ? ScanMode.Seguro : ModeAdvanced.IsChecked == true ? ScanMode.Avanzado : ScanMode.Moderado;

    private async void Scan_Click(object sender, RoutedEventArgs e)
    {
        ScanBtn.IsEnabled = DeleteBtn.IsEnabled = false;
        Progress.Visibility = Visibility.Visible;
        StatusText.Text = "Buscando...";
        var mode = Mode;
        _items = await Task.Run(() => UninstallerService.FindLeftovers(_program, mode, CancellationToken.None));
        _items = _items.OrderBy(i => i.IsRegistry).ThenBy(i => i.Display).ToList();
        Grid.ItemsSource = _items;
        Progress.Visibility = Visibility.Collapsed;
        ScanBtn.IsEnabled = true;
        DeleteBtn.IsEnabled = _items.Count > 0;
        StatusText.Text = _items.Count == 0
            ? "No se encontraron restos. ¡El programa se eliminó por completo!"
            : $"{_items.Count} elementos encontrados · {Util.FormatBytes(_items.Sum(i => i.Size))}";
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        Grid.CommitEdit(DataGridEditingUnit.Row, true);
        var sel = _items.Where(i => i.Selected).ToList();
        if (sel.Count == 0) return;
        if (MessageBox.Show($"Se eliminarán {sel.Count} elementos de forma permanente.\nLas claves del registro se guardan antes en una copia de seguridad.\n\n¿Continuar?",
                "Eliminar restos", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        DeleteBtn.IsEnabled = ScanBtn.IsEnabled = false;
        Progress.Visibility = Visibility.Visible;
        var (ok, failed) = await Task.Run(() => UninstallerService.DeleteLeftovers(sel));
        Progress.Visibility = Visibility.Collapsed;
        _items = _items.Except(sel).ToList();
        Grid.ItemsSource = _items;
        ScanBtn.IsEnabled = true;
        DeleteBtn.IsEnabled = _items.Count > 0;
        StatusText.Text = $"Eliminados: {ok}" + (failed > 0 ? $" · No se pudieron eliminar: {failed} (pueden estar en uso; reinicia y vuelve a intentarlo)" : "");
    }

    private void CheckAll_Click(object sender, RoutedEventArgs e) { _items.ForEach(i => i.Selected = true); Grid.Items.Refresh(); }
    private void UncheckAll_Click(object sender, RoutedEventArgs e) { _items.ForEach(i => i.Selected = false); Grid.Items.Refresh(); }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
