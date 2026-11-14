using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using OpenSweep.Core;
using OpenSweep.Services;
using OpenSweep.Views;

namespace OpenSweep;

// Clase (no record): la lista necesita comparar por referencia. Con un record, al guardar la página
// en Instance cambiaba su hash y el ListBox ya no podía cambiar la selección.
public sealed class NavItem(string icon, string title, Func<UserControl> factory)
{
    public string Icon { get; } = icon;
    public string Title { get; } = title;
    public Func<UserControl> Factory { get; } = factory;
    public string DisplayTitle => Loc.T(Title);
    public UserControl? Instance { get; set; }
}

public partial class MainWindow : Window
{
    public static MainWindow? Current { get; private set; }

    private readonly List<NavItem> _items =
    [
        new("", Loc.T("Panel de salud"), () => new HealthView()),
        new("", Loc.T("Limpieza"), () => new CleanerView()),
        new("", Loc.T("Registro"), () => new RegistryCleanerView()),
        new("", Loc.T("Desinstalador"), () => new UninstallerView()),
        new("", Loc.T("Inicio de Windows"), () => new StartupView()),
        new("", Loc.T("Extensiones"), () => new ExtensionsView()),
        new("", Loc.T("Analizador de disco"), () => new DiskAnalyzerView()),
        new("", Loc.T("Archivos duplicados"), () => new DuplicatesView()),
        new("", Loc.T("Fotos y vídeos"), () => new PhotosView()),
        new("", Loc.T("Borrado seguro"), () => new WiperView()),
        new("", Loc.T("Actualizar programas"), () => new UpdaterView()),
        new("", Loc.T("Copias de seguridad"), () => new BackupsView()),
        new("", Loc.T("Opciones"), () => new SettingsView()),
    ];

    public MainWindow()
    {
        InitializeComponent();
        Current = this;
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = $"v{v?.Major}.{v?.Minor}.{v?.Build}";
        Nav.ItemsSource = _items;
        // Permite abrir una sección concreta: OpenSweep.exe --page 3
        var args = Environment.GetCommandLineArgs();
        var idx = Array.IndexOf(args, "--page");
        Nav.SelectedIndex = idx > 0 && idx + 1 < args.Length && int.TryParse(args[idx + 1], out var page) && page >= 0 && page < _items.Count ? page : 0;
    }

    private AppUpdate? _update;

    protected override async void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        if (!AppSettings.Current.CheckUpdates) return;
        _update = await AppUpdater.CheckAsync();
        if (_update == null) return;
        UpdateText.Text = $"{Loc.T("Hay una versión nueva de OpenSweep")}: {_update.Version}";
        UpdateBanner.Visibility = Visibility.Visible;
    }

    private void UpdateLater_Click(object sender, RoutedEventArgs e) => UpdateBanner.Visibility = Visibility.Collapsed;

    private async void UpdateNow_Click(object sender, RoutedEventArgs e)
    {
        if (_update == null) return;
        UpdateBtn.IsEnabled = false;
        var progress = new Progress<int>(p => UpdateText.Text = $"{Loc.T("Descargando actualización")}… {p}%");
        if (await AppUpdater.InstallAsync(_update, progress)) Application.Current.Shutdown();
        else UpdateBtn.IsEnabled = true;
    }

    public void Navigate(int index) => Nav.SelectedIndex = index;

    private void Nav_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Nav.SelectedItem is not NavItem item) return;
        item.Instance ??= item.Factory();
        PageHost.Content = item.Instance;
    }
}
