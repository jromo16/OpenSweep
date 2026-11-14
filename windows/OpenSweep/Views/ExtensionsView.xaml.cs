using System.Windows;
using System.Windows.Controls;
using OpenSweep.Core;
using OpenSweep.Services;

namespace OpenSweep.Views;

public partial class ExtensionsView : UserControl
{
    public ExtensionsView()
    {
        InitializeComponent();
        Loaded += async (_, _) => await Load();
    }

    private async Task Load()
    {
        var list = await Task.Run(BrowserExtensions.Scan);
        ExtGrid.ItemsSource = list;
        CountText.Text = $"{list.Count} {Loc.T("extensiones encontradas")}";
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await Load();

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (ExtGrid.SelectedItem is BrowserExtension ext) Util.OpenInExplorer(ext.Folder);
    }

    private void Store_Click(object sender, RoutedEventArgs e)
    {
        if (ExtGrid.SelectedItem is not BrowserExtension ext) return;
        var url = ext.Browser switch
        {
            "Microsoft Edge" => $"https://microsoftedge.microsoft.com/addons/detail/{ext.Id}",
            "Mozilla Firefox" => $"https://addons.mozilla.org/es/firefox/search/?q={Uri.EscapeDataString(ext.Name)}",
            _ => $"https://chromewebstore.google.com/detail/{ext.Id}",
        };
        Util.OpenUrl(url);
    }

    private async void Remove_Click(object sender, RoutedEventArgs e)
    {
        var sel = ExtGrid.SelectedItems.OfType<BrowserExtension>().ToList();
        if (sel.Count == 0) return;
        var procs = sel.SelectMany(s => s.Processes).Distinct().ToArray();
        if (Util.IsProcessRunning(procs))
        {
            if (Dialogs.Show(Loc.T("El navegador debe cerrarse para eliminar extensiones. ¿Cerrarlo ahora?"), "OpenSweep",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            Util.KillProcesses(procs);
        }
        if (Dialogs.Show($"{Loc.T("¿Eliminar")} {sel.Count} {Loc.T("extensiones?")}\n\nNota: si tienes la sincronización activada, el navegador podría reinstalarlas; en ese caso quítalas también desde el propio navegador.",
                "OpenSweep", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        int ok = await Task.Run(() => sel.Count(BrowserExtensions.Remove));
        Dialogs.Show($"{Loc.T("Se eliminaron")} {ok} de {sel.Count} {Loc.T("extensiones.")}", "OpenSweep");
        await Load();
    }
}
