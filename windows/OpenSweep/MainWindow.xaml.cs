using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using OpenSweep.Core;
using OpenSweep.Services;
using OpenSweep.Views;

namespace OpenSweep;

public sealed record NavItem(string Icon, string Title, Func<UserControl> Factory)
{
    public UserControl? Instance { get; set; }
}

public partial class MainWindow : Window
{
    public static MainWindow? Current { get; private set; }

    private readonly List<NavItem> _items =
    [
        new("", "Panel de salud", () => new HealthView()),
        new("", "Limpieza", () => new CleanerView()),
        new("", "Registro", () => new RegistryCleanerView()),
        new("", "Desinstalador", () => new UninstallerView()),
        new("", "Inicio de Windows", () => new StartupView()),
        new("", "Extensiones", () => new ExtensionsView()),
        new("", "Analizador de disco", () => new DiskAnalyzerView()),
        new("", "Archivos duplicados", () => new DuplicatesView()),
        new("", "Fotos y vídeos", () => new PhotosView()),
        new("", "Borrado seguro", () => new WiperView()),
        new("", "Actualizar programas", () => new UpdaterView()),
        new("", "Copias de seguridad", () => new BackupsView()),
        new("", "Opciones", () => new SettingsView()),
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
