using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using OpenSweep.Core;
using OpenSweep.Services;

namespace OpenSweep.Views;

public partial class PhotosView : UserControl
{
    private readonly ObservableCollection<string> _folders = [];
    private MediaAnalysis? _result;
    private CancellationTokenSource? _cts;

    public PhotosView()
    {
        InitializeComponent();
        foreach (var f in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                     Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
                     Path.Combine(Util.UserProfile, "Downloads"),
                     Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                     Path.Combine(Util.UserProfile, "OneDrive", "Imágenes"),
                     Path.Combine(Util.UserProfile, "OneDrive", "Pictures"),
                 })
            if (Directory.Exists(f) && !_folders.Contains(f)) _folders.Add(f);
#if DEBUG
        // Solo en desarrollo: --photos-dir <carpeta> analiza únicamente esa carpeta (para pruebas automáticas)
        var dbg = Environment.GetCommandLineArgs();
        var di = Array.IndexOf(dbg, "--photos-dir");
        if (di >= 0 && di + 1 < dbg.Length) { _folders.Clear(); _folders.Add(dbg[di + 1]); }
#endif
        FoldersList.ItemsSource = _folders;
        foreach (TabItem t in Tabs.Items) t.Content = MakeList();
    }

    /// <summary>Lista virtualizada (solo crea las filas visibles) sin resaltar la selección.</summary>
    private ListBox MakeList()
    {
        var lb = new ListBox
        {
            ItemTemplate = (DataTemplate)Resources["GroupTemplate"],
            Background = System.Windows.Media.Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Margin = new Thickness(0, 10, 0, 0),
        };
        VirtualizingPanel.SetIsVirtualizing(lb, true);
        VirtualizingPanel.SetVirtualizationMode(lb, VirtualizationMode.Recycling);
        VirtualizingPanel.SetScrollUnit(lb, ScrollUnit.Pixel);
        ScrollViewer.SetHorizontalScrollBarVisibility(lb, ScrollBarVisibility.Disabled);
        var style = new Style(typeof(ListBoxItem));
        style.Setters.Add(new Setter(FocusableProperty, false));
        var template = new ControlTemplate(typeof(ListBoxItem));
        template.VisualTree = new FrameworkElementFactory(typeof(ContentPresenter));
        style.Setters.Add(new Setter(TemplateProperty, template));
        lb.ItemContainerStyle = style;
        return lb;
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

    private async void Scan_Click(object sender, RoutedEventArgs e)
    {
        if (_folders.Count == 0) return;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        int threshold = int.Parse((string)((ComboBoxItem)ThresholdBox.SelectedItem).Tag);
        var roots = _folders.ToList();
        ScanBtn.IsEnabled = false;
        CancelBtn.Visibility = Bar.Visibility = Visibility.Visible;
        var progress = new Progress<string>(s => StatusText.Text = s);
        try
        {
            _result = await Task.Run(() => PhotoAnalyzer.Analyze(roots, threshold, progress, ct), ct);
            ShowResults();
        }
        catch (OperationCanceledException) { StatusText.Text = Loc.T("Análisis cancelado."); }
        finally
        {
            ScanBtn.IsEnabled = true;
            CancelBtn.Visibility = Bar.Visibility = Visibility.Collapsed;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

    /// <summary>Las listas planas (borrosas, oscuras, capturas) se agrupan por mes.</summary>
    private static List<MediaGroup> ByMonth(List<MediaItem> items) =>
        items.GroupBy(i => new DateTime(i.Modified.Year, i.Modified.Month, 1))
             .OrderByDescending(g => g.Key)
             .Select(g => new MediaGroup
             {
                 Title = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(g.Key.ToString("MMMM yyyy")),
                 Items = g.ToList(),
             })
             .ToList();

    private void ShowResults()
    {
        if (_result == null) return;
        void Set(TabItem tab, string title, List<MediaGroup> groups, int count)
        {
            tab.Header = $"{title} ({count})";
            ((ListBox)tab.Content).ItemsSource = groups.ToList();   // copia nueva: obliga a la lista a refrescarse
        }
        Set(TabSimilar, Loc.T("Fotos similares"), _result.Similar, _result.Similar.Sum(g => g.Items.Count));
        Set(TabVideos, Loc.T("Vídeos similares"), _result.SimilarVideos, _result.SimilarVideos.Sum(g => g.Items.Count));
        Set(TabBlurry, Loc.T("Borrosas"), ByMonth(_result.Blurry), _result.Blurry.Count);
        Set(TabDark, Loc.T("Oscuras"), ByMonth(_result.Dark), _result.Dark.Count);
        Set(TabShots, Loc.T("Capturas de pantalla"), ByMonth(_result.Screenshots), _result.Screenshots.Count);
        UpdateStatus();
    }

    private IEnumerable<MediaItem> CurrentItems() =>
        (((Tabs.SelectedItem as TabItem)?.Content as ListBox)?.ItemsSource as IEnumerable<MediaGroup>)?.SelectMany(g => g.Items) ?? [];

    private void UpdateStatus()
    {
        if (_result == null) return;
        var sel = CurrentItems().Where(i => i.Selected).ToList();
        StatusText.Text = $"{_result.Total:N0} {Loc.T("fotos y vídeos analizados · marcados en esta pestaña")}: {sel.Count} ({Util.FormatBytes(sel.Sum(i => i.Size))})";
    }

    private void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source == Tabs) UpdateStatus();
    }

    private void Check_Click(object sender, RoutedEventArgs e) => UpdateStatus();

    private void Thumb_Click(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2 && sender is FrameworkElement { DataContext: MediaItem m })
        {
            try { Process.Start(new ProcessStartInfo(m.Path) { UseShellExecute = true }); } catch { }
        }
    }

    private void CheckAll_Click(object sender, RoutedEventArgs e)
    {
        // En fotos/vídeos similares nunca se marca la mejor de cada grupo
        foreach (var i in CurrentItems()) i.Selected = !i.IsBest;
        ShowResults();
    }

    private void Uncheck_Click(object sender, RoutedEventArgs e)
    {
        foreach (var i in CurrentItems()) i.Selected = false;
        ShowResults();
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_result == null) return;
        var tabList = (ListBox)((TabItem)Tabs.SelectedItem).Content;
        var groups = (tabList.ItemsSource as IEnumerable<MediaGroup>)?.ToList() ?? [];
        var sel = groups.SelectMany(g => g.Items).Where(i => i.Selected).ToList();
        if (sel.Count == 0) return;
        bool isSimilar = Tabs.SelectedItem == TabSimilar || Tabs.SelectedItem == TabVideos;
        if (isSimilar && groups.Any(g => g.Items.All(i => i.Selected)))
        {
            Dialogs.Show(Loc.T("Has marcado todas las fotos de algún grupo. Deja al menos una sin marcar en cada grupo."), "OpenSweep",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (Dialogs.Show($"{Loc.T("¿Enviar")} {sel.Count} {Loc.T("archivos")} ({Util.FormatBytes(sel.Sum(i => i.Size))}) {Loc.T("a la papelera?")}\n{Loc.T("Podrás recuperarlos desde la papelera de reciclaje.")}",
                "OpenSweep", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        var deleted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long freed = 0;
        await Dialogs.RunAsync(Loc.T("Enviando archivos a la papelera…"), sel,
            i =>
            {
                if (!Native.SendToRecycleBin(i.Path)) return false;
                lock (deleted) { deleted.Add(i.Path); freed += i.Size; }
                return true;
            },
            i => i.Name,
            (ok, failed) => $"{ok} {Loc.T("archivos enviados a la papelera")} · {Util.FormatBytes(freed)}" + (failed > 0 ? $" · {failed} {Loc.T("no se pudieron eliminar")}" : ""));
        void Remove(List<MediaItem> l) => l.RemoveAll(i => deleted.Contains(i.Path));
        Remove(_result.Blurry); Remove(_result.Dark); Remove(_result.Screenshots);
        // Los grupos se RECONSTRUYEN con listas nuevas: modificar una lista que la pantalla ya está mostrando
        // (sin avisarle) provocaba el error «Index was out of range» al redibujar.
        void Rebuild(List<MediaGroup> groups)
        {
            var rebuilt = groups
                .Select(g => new MediaGroup { Title = g.Title, Items = g.Items.Where(i => !deleted.Contains(i.Path)).ToList() })
                .Where(g => g.Items.Count >= 2).ToList();
            groups.Clear();
            groups.AddRange(rebuilt);
        }
        Rebuild(_result.Similar);
        Rebuild(_result.SimilarVideos);
        ShowResults();
        StatusText.Text = $"{Loc.T("Se enviaron")} {deleted.Count} {Loc.T("archivos a la papelera.")}";
    }
}
