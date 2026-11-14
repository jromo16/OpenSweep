using System.Windows;
using System.Windows.Controls;
using OpenSweep.Core;
using OpenSweep.Services;

namespace OpenSweep.Views;

public partial class UpdaterView : UserControl
{
    private List<UpdateItem> _items = [];

    public UpdaterView()
    {
        InitializeComponent();
    }

    private async void Check_Click(object sender, RoutedEventArgs e)
    {
        CheckBtn.IsEnabled = UpdateBtn.IsEnabled = false;
        Bar.Visibility = Visibility.Visible;
        StatusText.Text = Loc.T("Consultando winget...");
        bool available = await Task.Run(SoftwareUpdater.IsAvailable);
        if (!available)
        {
            Bar.Visibility = Visibility.Collapsed;
            CheckBtn.IsEnabled = true;
            StatusText.Text = Loc.T("winget no está instalado.");
            if (MessageBox.Show(Loc.T("El Administrador de paquetes de Windows (winget) no está disponible. ¿Abrir la Microsoft Store para instalar «Instalador de aplicación»?"),
                    "OpenSweep", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                Util.OpenUrl("ms-windows-store://pdp/?productid=9NBLGGH4NNS1");
            return;
        }
        _items = await Task.Run(SoftwareUpdater.CheckUpdates);
        UpdatesGrid.ItemsSource = _items;
        Bar.Visibility = Visibility.Collapsed;
        CheckBtn.IsEnabled = true;
        UpdateBtn.IsEnabled = _items.Count > 0;
        StatusText.Text = _items.Count == 0 ? Loc.T("Todos tus programas están actualizados.") : $"{_items.Count} {Loc.T("actualizaciones disponibles.")}";
    }

    private async void Update_Click(object sender, RoutedEventArgs e)
    {
        UpdatesGrid.CommitEdit(DataGridEditingUnit.Row, true);
        var sel = _items.Where(i => i.Selected).ToList();
        if (sel.Count == 0) return;
        CheckBtn.IsEnabled = UpdateBtn.IsEnabled = false;
        Bar.Visibility = Visibility.Visible;
        int ok = 0;
        foreach (var item in sel)
        {
            item.Status = Loc.T("Actualizando...");
            UpdatesGrid.Items.Refresh();
            StatusText.Text = $"{Loc.T("Actualizando")} {item.Name}...";
            bool success = await SoftwareUpdater.UpgradeAsync(item);
            item.Status = success ? Loc.T("Actualizado") : "Error";
            if (success) ok++;
            UpdatesGrid.Items.Refresh();
        }
        Bar.Visibility = Visibility.Collapsed;
        CheckBtn.IsEnabled = UpdateBtn.IsEnabled = true;
        StatusText.Text = $"{ok} de {sel.Count} {Loc.T("programas actualizados.")}";
    }
}
