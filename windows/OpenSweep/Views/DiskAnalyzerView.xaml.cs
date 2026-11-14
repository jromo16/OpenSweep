using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using OpenSweep.Core;
using OpenSweep.Services;

namespace OpenSweep.Views;

public partial class DiskAnalyzerView : UserControl
{
    private CancellationTokenSource? _cts;
    private List<FileEntry> _files = [];

    public DiskAnalyzerView()
    {
        InitializeComponent();
        DriveBox.ItemsSource = DriveInfo.GetDrives().Where(d => d.IsReady).Select(d => d.Name).ToList();
        DriveBox.SelectedIndex = 0;
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog();
        if (dlg.ShowDialog() != true) return;
        var items = (List<string>)DriveBox.ItemsSource;
        if (!items.Contains(dlg.FolderName)) items.Insert(0, dlg.FolderName);
        DriveBox.ItemsSource = null;
        DriveBox.ItemsSource = items;
        DriveBox.SelectedItem = dlg.FolderName;
    }

    private async void Analyze_Click(object sender, RoutedEventArgs e)
    {
        if (DriveBox.SelectedItem is not string root) return;
        _cts = new CancellationTokenSource();
        AnalyzeBtn.IsEnabled = false;
        CancelBtn.Visibility = Visibility.Visible;
        var progress = new Progress<string>(s => StatusText.Text = s);
        try
        {
            var ct = _cts.Token;
            var result = await Task.Run(() => DiskTools.Analyze(root, 500, progress, ct), ct);
            CategoriesList.ItemsSource = result.Categories;
            FoldersList.ItemsSource = result.Folders.Take(25).ToList();
            _files = result.LargestFiles;
            FilesGrid.ItemsSource = _files;
            StatusText.Text = $"{result.TotalFiles:N0} archivos · {Util.FormatBytes(result.TotalBytes)} en {root}";
        }
        catch (OperationCanceledException) { StatusText.Text = "Análisis cancelado."; }
        finally
        {
            AnalyzeBtn.IsEnabled = true;
            CancelBtn.Visibility = Visibility.Collapsed;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (FilesGrid.SelectedItem is FileEntry f) Util.OpenInExplorer(f.Path);
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        var sel = FilesGrid.SelectedItems.OfType<FileEntry>().ToList();
        if (sel.Count == 0) return;
        if (MessageBox.Show($"¿Enviar {sel.Count} archivos ({Util.FormatBytes(sel.Sum(f => f.Size))}) a la papelera?", "OpenSweep",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        foreach (var f in sel)
            if (Native.SendToRecycleBin(f.Path)) _files.Remove(f);
        FilesGrid.Items.Refresh();
    }
}
