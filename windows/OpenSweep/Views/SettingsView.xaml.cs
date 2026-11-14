using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using OpenSweep.Core;
using OpenSweep.Services;

namespace OpenSweep.Views;

public partial class SettingsView : UserControl
{
    private readonly AppSettings _s = AppSettings.Current;
    private bool _loading = true;

    public SettingsView()
    {
        InitializeComponent();
        SecureDelete.IsChecked = _s.SecureDeleteOnClean;
        CloseBrowsers.IsChecked = _s.CloseBrowsersAutomatically;
        RestorePoint.IsChecked = _s.CreateRestorePointBeforeUninstall;
        RegBackup.IsChecked = _s.BackupRegistryBeforeFix;
        UpdatesBox.IsChecked = _s.CheckUpdates;
        TrayBox.IsChecked = _s.TrayMonitor;
        Select(TempAge, _s.TempMinAgeHours.ToString(), 1);
        Select(AutoCleanBox, _s.AutoClean, 0);
        Select(ThresholdBox, _s.TrayThresholdMb.ToString(), 1);
        Select(LanguageBox, _s.Language, 0);
        Select(ThemeBox, _s.Theme, 0);
        IncludeList.ItemsSource = _s.CustomFolders;
        ExcludeList.ItemsSource = _s.Exclusions;
        CookieList.ItemsSource = _s.CookieWhitelist;
        _loading = false;
    }

    private static void Select(ComboBox box, string tag, int fallback) =>
        box.SelectedItem = box.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == tag) ?? box.Items[fallback];

    private static string Tag(ComboBox box) => (string)((ComboBoxItem)box.SelectedItem).Tag;

    private void Save(object? sender = null, RoutedEventArgs? e = null)
    {
        if (_loading) return;
        _s.SecureDeleteOnClean = SecureDelete.IsChecked == true;
        _s.CloseBrowsersAutomatically = CloseBrowsers.IsChecked == true;
        _s.CreateRestorePointBeforeUninstall = RestorePoint.IsChecked == true;
        _s.BackupRegistryBeforeFix = RegBackup.IsChecked == true;
        _s.CheckUpdates = UpdatesBox.IsChecked == true;
        _s.TempMinAgeHours = int.Parse(Tag(TempAge));
        _s.TrayThresholdMb = int.Parse(Tag(ThresholdBox));
        _s.Language = Tag(LanguageBox);
        _s.Save();
    }

    private void Theme_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        _s.Theme = Tag(ThemeBox);
        _s.Save();
        ThemeManager.Apply(_s.Theme);   // se aplica al instante
    }

    private void TempAge_Changed(object sender, SelectionChangedEventArgs e) => Save();
    private void Threshold_Changed(object sender, SelectionChangedEventArgs e) => Save();

    private void Language_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        Save();
        if (Dialogs.Show(Loc.T("¿Reiniciar OpenSweep ahora para aplicar el idioma?"), "OpenSweep", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
        {
            try { Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true }); } catch { }
            Application.Current.Shutdown();
        }
    }

    private async void AutoClean_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        var mode = Tag(AutoCleanBox);
        bool ok = await Task.Run(() => Scheduler.SetAutoClean(mode));
        if (!ok) { Dialogs.Show(Loc.T("No se pudo crear la tarea programada."), "OpenSweep"); return; }
        _s.AutoClean = mode;
        _s.Save();
    }

    private async void Tray_Click(object sender, RoutedEventArgs e)
    {
        bool on = TrayBox.IsChecked == true;
        bool ok = await Task.Run(() => Scheduler.SetTray(on));
        if (!ok) { Dialogs.Show(Loc.T("No se pudo crear la tarea programada."), "OpenSweep"); TrayBox.IsChecked = !on; return; }
        _s.TrayMonitor = on;
        _s.Save();
        // Arrancar el monitor ahora mismo, sin esperar al próximo inicio de sesión
        if (on) try { Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--tray") { UseShellExecute = true }); } catch { }
    }

    private void Log_Click(object sender, RoutedEventArgs e)
    {
        if (!File.Exists(Scheduler.LogFile)) { Dialogs.Show(Loc.T("Todavía no hay actividad registrada."), "OpenSweep"); return; }
        try { Process.Start(new ProcessStartInfo(Scheduler.LogFile) { UseShellExecute = true }); } catch { }
    }

    // ---------------- Cookies ----------------
    private void AddCookieDomain(string d)
    {
        d = d.Trim().ToLowerInvariant().Replace("https://", "").Replace("http://", "").TrimStart('.').Split('/')[0];
        if (d.StartsWith("www.")) d = d[4..];
        if (d.Length < 3 || !d.Contains('.') || _s.CookieWhitelist.Contains(d)) return;
        _s.CookieWhitelist.Add(d);
    }

    private void RefreshCookies()
    {
        _s.CookieWhitelist.Sort();
        CookieList.Items.Refresh();
        _s.Save();
    }

    private void AddCookie_Click(object sender, RoutedEventArgs e)
    {
        AddCookieDomain(CookieBox.Text);
        CookieBox.Text = "";
        RefreshCookies();
    }

    private void CommonCookies_Click(object sender, RoutedEventArgs e)
    {
        foreach (var d in CookieKeeper.CommonSites) AddCookieDomain(d);
        RefreshCookies();
    }

    private void RemoveCookie_Click(object sender, RoutedEventArgs e)
    {
        foreach (var d in CookieList.SelectedItems.OfType<string>().ToList()) _s.CookieWhitelist.Remove(d);
        RefreshCookies();
    }

    private async void BrowserCookies_Click(object sender, RoutedEventArgs e)
    {
        var all = await Task.Run(() =>
        {
            var total = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var db in CookieKeeper.AllCookieDatabases())
                foreach (var (k, v) in CookieKeeper.Domains(db)) total[k] = total.GetValueOrDefault(k) + v;
            return total;
        });
        if (all.Count == 0)
        {
            Dialogs.Show(Loc.T("No se pudieron leer las cookies. Cierra los navegadores y vuelve a intentarlo."), "OpenSweep");
            return;
        }
        // Ventana simple de selección múltiple
        var list = new ListBox { SelectionMode = SelectionMode.Multiple, Height = 380 };
        foreach (var d in all.OrderByDescending(x => x.Value).Select(x => x.Key)) list.Items.Add(d);
        var ok = new Button { Content = Loc.T("Añadir seleccionados"), Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Right, Padding = new Thickness(16, 6, 16, 6) };
        var panel = new DockPanel { Margin = new Thickness(16) };
        DockPanel.SetDock(ok, Dock.Bottom);
        panel.Children.Add(ok);
        panel.Children.Add(list);
        var w = new Window
        {
            Title = Loc.T("Sitios con cookies"), Width = 420, Height = 500, Content = panel,
            Owner = Window.GetWindow(this), WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false,
        };
        ok.Click += (_, _) => { foreach (var d in list.SelectedItems.OfType<string>()) AddCookieDomain(d); w.Close(); };
        w.ShowDialog();
        RefreshCookies();
    }

    // ---------------- Carpetas ----------------
    private static string? PickFolder()
    {
        var dlg = new OpenFolderDialog();
        return dlg.ShowDialog() == true ? dlg.FolderName : null;
    }

    private void AddInclude_Click(object sender, RoutedEventArgs e)
    {
        var f = PickFolder();
        if (f == null) return;
        if (PathHelper.IsProtectedFolder(f))
        {
            Dialogs.Show(Loc.T("No se puede incluir una carpeta del sistema o del perfil de usuario completa."), "OpenSweep", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!_s.CustomFolders.Contains(f)) _s.CustomFolders.Add(f);
        IncludeList.Items.Refresh(); _s.Save();
    }

    private void RemoveInclude_Click(object sender, RoutedEventArgs e)
    {
        if (IncludeList.SelectedItem is string f) { _s.CustomFolders.Remove(f); IncludeList.Items.Refresh(); _s.Save(); }
    }

    private void AddExclude_Click(object sender, RoutedEventArgs e)
    {
        var f = PickFolder();
        if (f == null) return;
        if (!_s.Exclusions.Contains(f)) _s.Exclusions.Add(f);
        ExcludeList.Items.Refresh(); _s.Save();
    }

    private void RemoveExclude_Click(object sender, RoutedEventArgs e)
    {
        if (ExcludeList.SelectedItem is string f) { _s.Exclusions.Remove(f); ExcludeList.Items.Refresh(); _s.Save(); }
    }

    private void GitHub_Click(object sender, RoutedEventArgs e) => Util.OpenUrl("https://github.com/jromo16/OpenSweep");

    private void Backups_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(AppSettings.BackupFolder);
        Util.OpenInExplorer(AppSettings.BackupFolder);
    }
}
