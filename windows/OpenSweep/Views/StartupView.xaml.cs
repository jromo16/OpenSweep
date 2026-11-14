using System.Windows;
using System.Windows.Controls;
using OpenSweep.Core;
using OpenSweep.Services;

namespace OpenSweep.Views;

public partial class StartupView : UserControl
{
    public StartupView()
    {
        InitializeComponent();
        Loaded += async (_, _) => await Load();
    }

    private async Task Load()
    {
        Bar.Visibility = Visibility.Visible;
        bool tasks = ShowTasks.IsChecked == true, ms = ShowMsTasks.IsChecked == true;
        var list = await Task.Run(() => StartupService.GetEntries(tasks, ms));
        EntriesGrid.ItemsSource = list;
        CountText.Text = $"{list.Count} {Loc.T("elementos")} · {list.Count(e => e.Enabled)} {Loc.T("habilitados")}";
        Bar.Visibility = Visibility.Collapsed;
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await Load();

    private List<StartupEntry> Selection() => EntriesGrid.SelectedItems.OfType<StartupEntry>().ToList();

    private async void Enable_Click(object sender, RoutedEventArgs e) => await SetEnabled(true);
    private async void Disable_Click(object sender, RoutedEventArgs e) => await SetEnabled(false);

    private async Task SetEnabled(bool enabled)
    {
        var sel = Selection();
        if (sel.Count == 0) return;
        if (sel.Any(s => s.Location.Contains("RunOnce")))
        {
            MessageBox.Show("Las entradas RunOnce se ejecutan una sola vez y no se pueden deshabilitar; puedes eliminarlas.", "OpenSweep");
        }
        var failed = await Task.Run(() => sel.Count(s => !StartupService.SetEnabled(s, enabled)));
        if (failed > 0) MessageBox.Show($"{failed} {Loc.T("elementos no se pudieron modificar.")}", "OpenSweep", MessageBoxButton.OK, MessageBoxImage.Warning);
        await Load();
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        var sel = Selection();
        if (sel.Count == 0) return;
        if (MessageBox.Show($"{Loc.T("¿Eliminar")} {sel.Count} {Loc.T("elementos del inicio? Esta acción no se puede deshacer.")}\n\n{Loc.T("Si solo quieres que no arranquen, usa «Deshabilitar».")}",
                "OpenSweep", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        await Task.Run(() => sel.ForEach(s => StartupService.Delete(s)));
        await Load();
    }

    private void OpenLocation_Click(object sender, RoutedEventArgs e)
    {
        var s = Selection().FirstOrDefault();
        if (s == null) return;
        var path = s.Kind == StartupKind.StartupFolder ? s.FilePath : PathHelper.ExtractPath(s.Command);
        if (path != null) Util.OpenInExplorer(path);
    }
}
