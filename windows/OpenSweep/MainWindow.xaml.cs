using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using OpenSweep.Core;
using OpenSweep.Services;
using OpenSweep.Views;

namespace OpenSweep;

// Clase (no record): la lista necesita comparar por referencia. Con un record, al guardar la página
// en Instance cambiaba su hash y el ListBox ya no podía cambiar la selección.
public sealed class NavItem(string group, string icon, string color, string title, Type page)
{
    public Type Page { get; } = page;
    public string Group { get; } = Loc.T(group);
    public string Icon { get; } = icon;
    public Brush Color { get; } = (Brush)new BrushConverter().ConvertFromString(color)!;
    public string Title { get; } = title;
    public string DisplayTitle => Loc.T(Title);
    public UserControl? Instance { get; set; }
}

public partial class MainWindow : Window
{
    public static MainWindow? Current { get; private set; }

    // Orden del menú = índice usado por --page
    private readonly List<NavItem> _items =
    [
        new("General", "", "#10B981", "Panel de salud", typeof(HealthView)),
        new("Limpieza", "", "#0A84FF", "Limpieza", typeof(CleanerView)),
        new("Limpieza", "", "#5E5CE6", "Registro", typeof(RegistryCleanerView)),
        new("Programas", "", "#BF5AF2", "Desinstalador", typeof(UninstallerView)),
        new("Programas", "", "#FF9F0A", "Inicio de Windows", typeof(StartupView)),
        new("Programas", "", "#30B0C7", "Extensiones", typeof(ExtensionsView)),
        new("Archivos", "", "#64A0FF", "Analizador de disco", typeof(DiskAnalyzerView)),
        new("Archivos", "", "#FF375F", "Archivos duplicados", typeof(DuplicatesView)),
        new("Archivos", "", "#FF9500", "Fotos y vídeos", typeof(PhotosView)),
        new("Archivos", "", "#FF453A", "Borrado seguro", typeof(WiperView)),
        new("Programas", "", "#32B65A", "Actualizar programas", typeof(UpdaterView)),
        new("OpenSweep", "", "#8E8E93", "Copias de seguridad", typeof(BackupsView)),
        new("OpenSweep", "", "#8E8E93", "Opciones", typeof(SettingsView)),
    ];

    public MainWindow()
    {
        InitializeComponent();
        Current = this;
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = $"v{v?.Major}.{v?.Minor}.{v?.Build}";

        // Agrupar el menú por secciones (General, Limpieza, Programas, Archivos, OpenSweep)
        var order = new[] { "General", "Limpieza", "Programas", "Archivos", "OpenSweep" }.Select(Loc.T).ToList();
        var view = new ListCollectionView(_items.OrderBy(i => order.IndexOf(i.Group)).ToList());
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(NavItem.Group)));
        Nav.ItemsSource = view;

        // Permite abrir una sección concreta: OpenSweep.exe --page 3
        var args = Environment.GetCommandLineArgs();
        var idx = Array.IndexOf(args, "--page");
        var start = idx > 0 && idx + 1 < args.Length && int.TryParse(args[idx + 1], out var page) && page >= 0 && page < _items.Count ? page : 0;
        Nav.SelectedItem = _items[start];

        // Sin Mica (Windows 10): fondo sólido
        if (!ThemeManager.SupportsMica) FallbackBackground.Visibility = Visibility.Visible;
        ThemeManager.ApplyToWindow(this);
    }

    private AppUpdate? _update;

    protected override async void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
#if DEBUG
        // Solo en desarrollo: OpenSweep.exe --demo muestra los diálogos sin borrar nada
        if (Environment.GetCommandLineArgs().Contains("--demo"))
        {
            if (Dialogs.Show($"{Loc.T("¿Enviar")} 2 {Loc.T("archivos")} (7.97 MB) {Loc.T("a la papelera?")}\n{Loc.T("Podrás recuperarlos desde la papelera de reciclaje.")}",
                    "OpenSweep", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                await Dialogs.RunAsync(Loc.T("Enviando archivos a la papelera…"), Enumerable.Range(1, 40).ToList(),
                    i => { Thread.Sleep(150); return true; }, i => $"IMG_{i:000}.jpg",
                    (ok, bad) => $"{ok} {Loc.T("archivos enviados a la papelera")} · 7.97 MB");
            return;
        }
#endif
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

    private int _toastId;

    /// <summary>Muestra un aviso breve en la parte inferior que desaparece solo.</summary>
    public async void ShowToast(string text)
    {
        var id = ++_toastId;
        ToastText.Text = text;
        ToastHost.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(1, TimeSpan.FromMilliseconds(180)));
        await Task.Delay(3500);
        if (id != _toastId) return;   // llegó otro aviso mientras tanto
        ToastHost.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, TimeSpan.FromMilliseconds(400)));
    }

    /// <summary>Abre la sección de un tipo de página concreto.</summary>
    public void Navigate<T>() where T : UserControl
    {
        var item = _items.FirstOrDefault(i => i.Page == typeof(T));
        if (item != null) Nav.SelectedItem = item;
    }

    private void Nav_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Nav.SelectedItem is not NavItem item) return;
        item.Instance ??= (UserControl)Activator.CreateInstance(item.Page)!;
        PageHost.Content = item.Instance;
    }
}
