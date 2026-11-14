using System.Reflection;
using System.Windows;
using System.Windows.Controls;
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
        new("", "Borrado seguro", () => new WiperView()),
        new("", "Actualizar programas", () => new UpdaterView()),
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

    public void Navigate(int index) => Nav.SelectedIndex = index;

    private void Nav_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Nav.SelectedItem is not NavItem item) return;
        item.Instance ??= item.Factory();
        PageHost.Content = item.Instance;
    }
}
