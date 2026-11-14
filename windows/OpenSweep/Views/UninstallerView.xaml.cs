using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using OpenSweep.Core;
using OpenSweep.Services;

namespace OpenSweep.Views;

public partial class UninstallerView : UserControl
{
    private List<InstalledProgram> _programs = [];
    private List<StoreApp> _storeApps = [];

    public UninstallerView()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            TracesGrid.ItemsSource = InstallMonitor.LoadAll();
            if (_programs.Count == 0) await LoadPrograms();
        };
    }

    private async Task LoadPrograms()
    {
        LoadingBar.Visibility = Visibility.Visible;
        bool sys = ShowSystem.IsChecked == true;
        _programs = await Task.Run(() => UninstallerService.GetInstalledPrograms(sys));
        ApplyFilter();
        LoadingBar.Visibility = Visibility.Collapsed;
        // Los iconos se cargan en segundo plano para no bloquear la lista
        var list = _programs;
        await Task.Run(() => { foreach (var p in list) UninstallerService.LoadIcon(p); });
        ProgramsGrid.Items.Refresh();
    }

    private void ApplyFilter()
    {
        var q = SearchBox.Text.Trim();
        var filtered = string.IsNullOrEmpty(q)
            ? _programs
            : _programs.Where(p => p.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || p.Publisher.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        ProgramsGrid.ItemsSource = filtered;
        CountText.Text = $"{filtered.Count} programas · {Util.FormatBytes(filtered.Sum(p => p.SizeBytes))} en total";
    }

    private void Search_Changed(object sender, TextChangedEventArgs e) => ApplyFilter();
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadPrograms();

    private InstalledProgram? Selected()
    {
        if (ProgramsGrid.SelectedItem is InstalledProgram p) return p;
        MessageBox.Show("Selecciona un programa de la lista.", "OpenSweep", MessageBoxButton.OK, MessageBoxImage.Information);
        return null;
    }

    private void ProgramsGrid_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (ProgramsGrid.SelectedItem is InstalledProgram) Uninstall_Click(sender, e);
    }

    private async void Uninstall_Click(object sender, RoutedEventArgs e)
    {
        var p = Selected();
        if (p == null) return;
        if (MessageBox.Show($"¿Desinstalar «{p.Name}»?\n\nSe ejecutará su desinstalador y después podrás buscar los restos.", "Desinstalar",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        UninstallBtn.IsEnabled = false;
        LoadingBar.Visibility = Visibility.Visible;
        try
        {
            if (AppSettings.Current.CreateRestorePointBeforeUninstall)
            {
                CountText.Text = "Creando punto de restauración del sistema...";
                await Task.Run(() => UninstallerService.CreateRestorePoint($"OpenSweep: antes de desinstalar {p.Name}"));
            }
            CountText.Text = $"Ejecutando el desinstalador de {p.Name}...";
            try
            {
                await UninstallerService.RunUninstallerAsync(p, quiet: false);
            }
            catch (Exception ex)
            {
                if (MessageBox.Show($"No se pudo ejecutar el desinstalador:\n{ex.Message}\n\n¿Quieres hacer una desinstalación forzada?",
                        "OpenSweep", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            }
        }
        finally
        {
            LoadingBar.Visibility = Visibility.Collapsed;
            UninstallBtn.IsEnabled = true;
        }

        new LeftoversWindow(p, forced: false) { Owner = Window.GetWindow(this) }.ShowDialog();
        await LoadPrograms();
    }

    private async void Forced_Click(object sender, RoutedEventArgs e)
    {
        var p = Selected();
        if (p == null) return;
        if (MessageBox.Show($"La desinstalación forzada NO ejecuta el desinstalador de «{p.Name}»: busca y elimina directamente sus archivos y claves del registro.\n\n¿Continuar?",
                "Desinstalación forzada", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        new LeftoversWindow(p, forced: true) { Owner = Window.GetWindow(this) }.ShowDialog();
        await LoadPrograms();
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var p = Selected();
        if (p == null) return;
        var dir = p.InstallLocation;
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
        {
            var exe = PathHelper.ExtractPath(p.DisplayIcon) ?? PathHelper.ExtractPath(p.UninstallString);
            dir = exe != null ? Path.GetDirectoryName(exe) ?? "" : "";
        }
        if (Directory.Exists(dir)) Util.OpenInExplorer(dir);
        else MessageBox.Show("No se conoce la carpeta de instalación de este programa.", "OpenSweep");
    }

    private void OpenRegistry_Click(object sender, RoutedEventArgs e)
    {
        var p = Selected();
        if (p == null) return;
        var root = p.Hive == RegistryHive.CurrentUser ? "HKEY_CURRENT_USER" : "HKEY_LOCAL_MACHINE";
        var path = p.View == RegistryView.Registry32 && p.Hive == RegistryHive.LocalMachine
            ? p.KeyPath.Replace(@"SOFTWARE\", @"SOFTWARE\WOW6432Node\")
            : p.KeyPath;
        try
        {
            using var k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Applets\Regedit");
            k.SetValue("LastKey", $@"{root}\{path}");
            foreach (var proc in Process.GetProcessesByName("regedit")) proc.Kill();
            Process.Start(new ProcessStartInfo("regedit.exe", "-m") { UseShellExecute = true });
        }
        catch { }
    }

    private void SearchWeb_Click(object sender, RoutedEventArgs e)
    {
        var p = Selected();
        if (p != null) Util.OpenUrl("https://www.bing.com/search?q=" + Uri.EscapeDataString(p.Name + " " + p.Publisher));
    }

    // ---------------- Monitor de instalaciones ----------------
    private async void Monitor_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Title = Loc.T("Elige el instalador"), Filter = "Instaladores (*.exe;*.msi)|*.exe;*.msi" };
        if (dlg.ShowDialog() != true) return;
        var installer = dlg.FileName;
        MonitorBtn.IsEnabled = false;
        TraceBar.Visibility = Visibility.Visible;
        var progress = new Progress<string>(s => TraceStatus.Text = s);
        try
        {
            TraceStatus.Text = Loc.T("Tomando una instantánea del sistema (puede tardar un minuto)…");
            var before = await Task.Run(() => InstallMonitor.Take(progress));

            TraceStatus.Text = Loc.T("Ejecuta la instalación normalmente…");
            var psi = installer.EndsWith(".msi", StringComparison.OrdinalIgnoreCase)
                ? new ProcessStartInfo("msiexec.exe", $"/i \"{installer}\"") { UseShellExecute = true }
                : new ProcessStartInfo(installer) { UseShellExecute = true };
            using (var proc = Process.Start(psi)) { if (proc != null) await proc.WaitForExitAsync(); }
            MessageBox.Show(Loc.T("Cuando la instalación haya terminado por completo (y hayas cerrado el instalador), pulsa Aceptar para registrar los cambios."),
                Loc.T("Monitor de instalaciones"), MessageBoxButton.OK, MessageBoxImage.Information);

            TraceStatus.Text = Loc.T("Comparando cambios…");
            var after = await Task.Run(() => InstallMonitor.Take(progress));
            var trace = InstallMonitor.Diff(before, after, Path.GetFileName(installer));
            InstallMonitor.Save(trace);
            TracesGrid.ItemsSource = InstallMonitor.LoadAll();
            TraceStatus.Text = $"«{trace.Name}»: {trace.Count} {Loc.T("elementos registrados")}";
            await LoadPrograms();
        }
        catch (Exception ex) { TraceStatus.Text = "Error: " + ex.Message; }
        finally
        {
            MonitorBtn.IsEnabled = true;
            TraceBar.Visibility = Visibility.Collapsed;
        }
    }

    private async void TraceUninstall_Click(object sender, RoutedEventArgs e)
    {
        if (TracesGrid.SelectedItem is not InstallTrace t) return;
        var program = await Task.Run(() => InstallMonitor.Program(t));
        if (program != null && MessageBox.Show($"{Loc.T("¿Ejecutar primero el desinstalador oficial de")} «{program.Name}»?", "OpenSweep",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            try { await UninstallerService.RunUninstallerAsync(program, quiet: false); } catch { }
        }
        var items = await Task.Run(() => InstallMonitor.RemainingItems(t));
        if (items.Count == 0)
        {
            MessageBox.Show(Loc.T("No queda ningún rastro de esta instalación."), "OpenSweep");
            return;
        }
        new LeftoversWindow(t.Name, items) { Owner = Window.GetWindow(this) }.ShowDialog();
        await LoadPrograms();
    }

    private void TraceDelete_Click(object sender, RoutedEventArgs e)
    {
        if (TracesGrid.SelectedItem is not InstallTrace t) return;
        try { File.Delete(t.FileName); } catch { }
        TracesGrid.ItemsSource = InstallMonitor.LoadAll();
    }

    // ---------------- Modo cazador ----------------
    private void Hunter_Down(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        HunterTarget.CaptureMouse();
        CountText.Text = "Suelta el botón sobre la ventana del programa que quieres desinstalar…";
    }

    private void Hunter_Up(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        HunterTarget.ReleaseMouseCapture();
        var pid = Native.ProcessUnderCursor();
        if (pid == 0 || pid == Environment.ProcessId) { ApplyFilter(); return; }
        string? exe = null;
        try { exe = Process.GetProcessById(pid).MainModule?.FileName; } catch { }
        if (exe == null) { CountText.Text = "No se pudo identificar esa ventana."; return; }
        ShowOwner(exe, pid);
    }

    private void Programs_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files) ShowOwner(files[0], 0);
    }

    /// <summary>Selecciona el programa al que pertenece el archivo y ofrece acciones.</summary>
    private void ShowOwner(string exe, int pid)
    {
        var owner = UninstallerService.FindOwner(exe, _programs);
        if (owner == null)
        {
            CountText.Text = $"{Path.GetFileName(exe)} no pertenece a ningún programa instalado de la lista.";
            if (pid != 0 && MessageBox.Show($"No se encontró el programa de «{Path.GetFileName(exe)}».\n\n¿Abrir su carpeta?", "Modo cazador",
                    MessageBoxButton.YesNo) == MessageBoxResult.Yes) Util.OpenInExplorer(exe);
            return;
        }
        SearchBox.Text = "";
        ApplyFilter();
        ProgramsGrid.SelectedItem = owner;
        ProgramsGrid.ScrollIntoView(owner);
        CountText.Text = $"Encontrado: {owner.Name}";

        var menu = new ContextMenu();
        void Add(string text, Action a) { var mi = new MenuItem { Header = text }; mi.Click += (_, _) => a(); menu.Items.Add(mi); }
        Add($"Desinstalar «{owner.Name}»", () => Uninstall_Click(this, new RoutedEventArgs()));
        Add("Desinstalación forzada", () => Forced_Click(this, new RoutedEventArgs()));
        Add("Abrir carpeta", () => Util.OpenInExplorer(exe));
        if (pid != 0) Add("Cerrar el programa", () => { try { Process.GetProcessById(pid).Kill(true); } catch { } });
        menu.PlacementTarget = HunterTarget;
        menu.IsOpen = true;
    }

    // ---------------- Store ----------------
    private async void LoadStore_Click(object sender, RoutedEventArgs e)
    {
        StoreBar.Visibility = Visibility.Visible;
        _storeApps = await Task.Run(UninstallerService.GetStoreApps);
        StoreGrid.ItemsSource = _storeApps;
        StoreCountText.Text = $"{_storeApps.Count} aplicaciones";
        StoreBar.Visibility = Visibility.Collapsed;
    }

    private async void RemoveStore_Click(object sender, RoutedEventArgs e)
    {
        StoreGrid.CommitEdit(DataGridEditingUnit.Row, true);
        var sel = _storeApps.Where(a => a.Selected).ToList();
        if (sel.Count == 0) return;
        if (MessageBox.Show($"¿Desinstalar {sel.Count} aplicaciones?\n\n" + string.Join("\n", sel.Select(a => "• " + a.Name)), "OpenSweep",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        StoreBar.Visibility = Visibility.Visible;
        RemoveStoreBtn.IsEnabled = false;
        int ok = 0;
        foreach (var a in sel)
        {
            StoreCountText.Text = $"Desinstalando {a.Name}...";
            if (await Task.Run(() => UninstallerService.RemoveStoreApp(a))) ok++;
        }
        RemoveStoreBtn.IsEnabled = true;
        MessageBox.Show($"Se desinstalaron {ok} de {sel.Count} aplicaciones.", "OpenSweep");
        LoadStore_Click(sender, e);
    }

    // ---------------- Forzada manual ----------------
    private void BrowseForced_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Carpeta de instalación del programa" };
        if (dlg.ShowDialog() == true)
        {
            ForcedFolder.Text = dlg.FolderName;
            if (string.IsNullOrWhiteSpace(ForcedName.Text)) ForcedName.Text = Path.GetFileName(dlg.FolderName);
        }
    }

    private void ForcedScan_Click(object sender, RoutedEventArgs e)
    {
        var name = ForcedName.Text.Trim();
        if (name.Length < 3)
        {
            MessageBox.Show("Escribe el nombre del programa (mínimo 3 letras).", "OpenSweep");
            return;
        }
        var folder = ForcedFolder.Text.Trim();
        if (folder.Length > 0 && PathHelper.IsProtectedFolder(folder))
        {
            MessageBox.Show("Esa carpeta es del sistema y no se puede eliminar.", "OpenSweep", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var fake = UninstallerService.FakeProgram(name, folder.Length > 0 ? folder : null);
        new LeftoversWindow(fake, forced: true) { Owner = Window.GetWindow(this) }.ShowDialog();
    }
}
