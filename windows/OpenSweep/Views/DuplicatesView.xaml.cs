using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using OpenSweep.Core;
using OpenSweep.Services;

namespace OpenSweep.Views;

public partial class DuplicatesView : UserControl
{
    private readonly ObservableCollection<string> _folders = [];
    private List<FileEntry> _files = [];
    private CancellationTokenSource? _cts;

    public DuplicatesView()
    {
        InitializeComponent();
        _folders.Add(Util.UserProfile);
        FoldersList.ItemsSource = _folders;
    }

    private void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Multiselect = true };
        if (dlg.ShowDialog() == true)
            foreach (var f in dlg.FolderNames) if (!_folders.Contains(f)) _folders.Add(f);
    }

    private void RemoveFolder_Click(object sender, RoutedEventArgs e)
    {
        if (FoldersList.SelectedItem is string s) _folders.Remove(s);
    }

    private async void Search_Click(object sender, RoutedEventArgs e)
    {
        if (_folders.Count == 0) return;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        SearchBtn.IsEnabled = false;
        CancelBtn.Visibility = Visibility.Visible;
        long min = long.Parse((string)((ComboBoxItem)MinSizeBox.SelectedItem).Tag);
        bool matchName = MatchName.IsChecked == true;
        var roots = _folders.ToList();
        var progress = new Progress<string>(s => StatusText.Text = s);
        try
        {
            var groups = await Task.Run(() => DiskTools.FindDuplicates(roots, min, matchName, progress, ct), ct);
            _files = groups.SelectMany(g => g).ToList();
            DupGrid.ItemsSource = _files;
            long wasted = groups.Sum(g => g[0].Size * (g.Count - 1));
            StatusText.Text = $"{groups.Count:N0} grupos de duplicados · {Util.FormatBytes(wasted)} recuperables";
        }
        catch (OperationCanceledException) { StatusText.Text = "Búsqueda cancelada."; }
        finally
        {
            SearchBtn.IsEnabled = true;
            CancelBtn.Visibility = Visibility.Collapsed;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (DupGrid.SelectedItem is FileEntry f) Util.OpenInExplorer(f.Path);
    }

    private void Uncheck_Click(object sender, RoutedEventArgs e)
    {
        _files.ForEach(f => f.Selected = false);
        DupGrid.Items.Refresh();
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        DupGrid.CommitEdit(DataGridEditingUnit.Row, true);
        var sel = _files.Where(f => f.Selected).ToList();
        if (sel.Count == 0) return;
        // Evitar borrar todas las copias de un grupo
        var fullGroups = _files.GroupBy(f => f.Group).Where(g => g.All(f => f.Selected)).Select(g => g.Key).ToList();
        if (fullGroups.Count > 0)
        {
            MessageBox.Show($"Has marcado todas las copias de {fullGroups.Count} grupo(s) (p. ej. grupo {fullGroups[0]}). Deja al menos una copia sin marcar en cada grupo.",
                "OpenSweep", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (MessageBox.Show($"¿Enviar {sel.Count} archivos duplicados ({Util.FormatBytes(sel.Sum(f => f.Size))}) a la papelera?", "OpenSweep",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        var deleted = await Task.Run(() => sel.Where(f => Native.SendToRecycleBin(f.Path)).ToList());
        _files = _files.Except(deleted).ToList();
        // quitar grupos que ya no tienen duplicados
        var single = _files.GroupBy(f => f.Group).Where(g => g.Count() < 2).SelectMany(g => g).ToList();
        _files = _files.Except(single).ToList();
        DupGrid.ItemsSource = _files;
        StatusText.Text = $"Se enviaron {deleted.Count} archivos a la papelera.";
    }
}
