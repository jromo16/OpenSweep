using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;
using OpenSweep.Core;
using OpenSweep.Services;

namespace OpenSweep.Views;

public partial class DiskAnalyzerView : UserControl
{
    private CancellationTokenSource? _cts;
    private List<FileEntry> _files = [];
    private DiskAnalysis? _analysis;
    private string _mapDir = "";

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
            _analysis = result;
            ShowMap(result.Root);
            StatusText.Text = $"{result.TotalFiles:N0} {Loc.T("archivos")} · {Util.FormatBytes(result.TotalBytes)} en {root}";
        }
        catch (OperationCanceledException) { StatusText.Text = Loc.T("Análisis cancelado."); }
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

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        var sel = FilesGrid.SelectedItems.OfType<FileEntry>().ToList();
        if (sel.Count == 0) return;
        if (Dialogs.Show($"{Loc.T("¿Enviar")} {sel.Count} {Loc.T("archivos")} ({Util.FormatBytes(sel.Sum(f => f.Size))}) {Loc.T("a la papelera?")}", "OpenSweep",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        var deleted = new List<FileEntry>();
        long freed = 0;
        await Dialogs.RunAsync(Loc.T("Enviando archivos a la papelera…"), sel,
            f =>
            {
                if (!Native.SendToRecycleBin(f.Path)) return false;
                lock (deleted) { deleted.Add(f); freed += f.Size; }
                return true;
            },
            f => f.Name,
            (ok, failed) => $"{ok} {Loc.T("archivos enviados a la papelera")} · {Util.FormatBytes(freed)}" + (failed > 0 ? $" · {failed} {Loc.T("no se pudieron eliminar")}" : ""));
        _files = _files.Except(deleted).ToList();   // lista nueva: no se modifica la que está en pantalla
        FilesGrid.ItemsSource = _files;
    }

    // ---------------- Mapa visual (treemap) ----------------
    private static readonly Color[] Palette =
    [
        Color.FromRgb(0x10, 0xB9, 0x81), Color.FromRgb(0x0A, 0x84, 0xFF), Color.FromRgb(0xBF, 0x5A, 0xF2), Color.FromRgb(0xFF, 0x9F, 0x0A),
        Color.FromRgb(0xFF, 0x37, 0x5F), Color.FromRgb(0x30, 0xB0, 0xC7), Color.FromRgb(0x5E, 0x5C, 0xE6), Color.FromRgb(0x32, 0xB6, 0x5A),
        Color.FromRgb(0xFF, 0x45, 0x3A), Color.FromRgb(0x64, 0xA0, 0xFF),
    ];

    private sealed record MapItem(string Path, string Label, long Size, bool IsFiles);

    private void ShowMap(string dir)
    {
        if (_analysis == null) return;
        _mapDir = dir;
        MapPath.Text = $"{dir}  ·  {Util.FormatBytes(_analysis.DirSize.GetValueOrDefault(dir))}";
        UpBtn.IsEnabled = dir.Length > _analysis.Root.Length;
        DrawMap();
    }

    private void DrawMap()
    {
        MapCanvas.Children.Clear();
        if (_analysis == null || MapCanvas.ActualWidth < 20 || MapCanvas.ActualHeight < 20) return;
        MapEmpty.Visibility = Visibility.Collapsed;

        var items = new List<(MapItem, double)>();
        if (_analysis.Children.TryGetValue(_mapDir, out var kids))
            foreach (var k in kids)
            {
                var size = _analysis.DirSize.GetValueOrDefault(k);
                items.Add((new MapItem(k, System.IO.Path.GetFileName(k), size, false), size));
            }
        var loose = _analysis.DirectFiles.GetValueOrDefault(_mapDir);
        if (loose > 0) items.Add((new MapItem(_mapDir, Loc.T("Archivos sueltos"), loose, true), loose));
        if (items.Count == 0) { MapEmpty.Text = Loc.T("Esta carpeta no tiene subcarpetas."); MapEmpty.Visibility = Visibility.Visible; return; }

        var layout = Treemap.Layout(items, new Rect(0, 0, MapCanvas.ActualWidth, MapCanvas.ActualHeight));
        int i = 0;
        foreach (var (item, rect) in layout)
        {
            if (rect.Width < 2 || rect.Height < 2) continue;
            var color = item.IsFiles ? Color.FromRgb(0x8E, 0x8E, 0x93) : Palette[i++ % Palette.Length];
            var block = new Border
            {
                Width = Math.Max(0, rect.Width - 2), Height = Math.Max(0, rect.Height - 2),
                Background = new SolidColorBrush(color), CornerRadius = new CornerRadius(5),
                ToolTip = $"{item.Path}\n{Util.FormatBytes(item.Size)}", Tag = item,
                Cursor = item.IsFiles ? Cursors.Arrow : Cursors.Hand,
            };
            // Texto solo si cabe
            if (rect.Width > 70 && rect.Height > 34)
                block.Child = new TextBlock
                {
                    Text = $"{item.Label}\n{Util.FormatBytes(item.Size)}", Foreground = Brushes.White, FontSize = rect.Width > 160 && rect.Height > 60 ? 13 : 11,
                    FontWeight = FontWeights.SemiBold, Margin = new Thickness(8, 6, 6, 6), TextTrimming = TextTrimming.CharacterEllipsis,
                };
            block.MouseLeftButtonDown += Block_MouseDown;
            block.MouseRightButtonUp += (_, _) => Util.OpenInExplorer(item.Path);
            block.MouseEnter += (_, _) => block.Opacity = 0.85;
            block.MouseLeave += (_, _) => block.Opacity = 1;
            Canvas.SetLeft(block, rect.X + 1);
            Canvas.SetTop(block, rect.Y + 1);
            MapCanvas.Children.Add(block);
        }
    }

    private void Block_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2 && sender is Border { Tag: MapItem { IsFiles: false } item }) ShowMap(item.Path);
    }

    private void Up_Click(object sender, RoutedEventArgs e)
    {
        if (_analysis == null || _mapDir.Length <= _analysis.Root.Length) return;
        var parent = System.IO.Path.GetDirectoryName(_mapDir);
        ShowMap(parent == null || parent.Length < _analysis.Root.Length ? _analysis.Root : parent);
    }

    private void OpenMapFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_mapDir.Length > 0) Util.OpenInExplorer(_mapDir);
    }

    private void MapCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => DrawMap();
}
