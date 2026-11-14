using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using OpenSweep.Core;

namespace OpenSweep.Views;

public partial class WiperView : UserControl
{
    private readonly ObservableCollection<string> _targets = [];
    private CancellationTokenSource? _cts;

    public WiperView()
    {
        InitializeComponent();
        ShredList.ItemsSource = _targets;
        DriveBox.ItemsSource = DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType is DriveType.Fixed or DriveType.Removable).Select(d => d.Name).ToList();
        DriveBox.SelectedIndex = 0;
    }

    private void AddFiles_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Multiselect = true, Title = Loc.T("Archivos a destruir") };
        if (dlg.ShowDialog() == true) foreach (var f in dlg.FileNames) if (!_targets.Contains(f)) _targets.Add(f);
    }

    private void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = Loc.T("Carpeta a destruir (con todo su contenido)") };
        if (dlg.ShowDialog() != true) return;
        if (PathHelper.IsProtectedFolder(dlg.FolderName))
        {
            Dialogs.Show(Loc.T("Esa carpeta es del sistema y no se puede destruir."), "OpenSweep", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!_targets.Contains(dlg.FolderName)) _targets.Add(dlg.FolderName);
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        foreach (var s in ShredList.SelectedItems.OfType<string>().ToList()) _targets.Remove(s);
    }

    private async void Shred_Click(object sender, RoutedEventArgs e)
    {
        if (_targets.Count == 0) return;
        if (Dialogs.Show($"{Loc.T("Se destruirán")} {_targets.Count} {Loc.T("elementos de forma IRRECUPERABLE.")}\n\n{Loc.T("¿Estás seguro?")}", Loc.T("Destruir archivos"),
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        int passes = int.Parse((string)((ComboBoxItem)ShredPasses.SelectedItem).Tag);
        ShredBtn.IsEnabled = false;
        var targets = _targets.ToList();
        // Lista completa de archivos (las carpetas se expanden) para poder mostrar el porcentaje
        var files = await Task.Run(() => targets.SelectMany(t => Directory.Exists(t) ? Util.SafeFiles(t).Select(f => f.FullName) : [t]).ToList());
        int ok = await Dialogs.RunAsync(Loc.T("Destruyendo archivos…"), files,
            f => SecureWipe.ShredFile(f, passes),
            f => Path.GetFileName(f),
            (done, bad) => $"{done} {Loc.T("archivos destruidos")}" + (bad > 0 ? $" · {bad} {Loc.T("no se pudieron destruir")}" : ""));
        int fail = files.Count - ok;
        foreach (var t in targets.Where(Directory.Exists)) Util.RemoveEmptyDirs(t, removeRoot: true);
        _targets.Clear();
        ShredBtn.IsEnabled = true;
        ShredStatus.Text = $"{ok} {Loc.T("archivos destruidos.")}" + (fail > 0 ? $" {fail} {Loc.T("no se pudieron destruir (en uso o sin permisos).")}" : "");
    }

    private async void Wipe_Click(object sender, RoutedEventArgs e)
    {
        if (DriveBox.SelectedItem is not string drive) return;
        if (Dialogs.Show($"{Loc.T("Se llenará el espacio libre de")} {drive} {Loc.T("temporalmente. Cierra otros programas y no copies archivos mientras tanto.")}\n\n{Loc.T("¿Continuar?")}",
                Loc.T("Borrar espacio libre"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        int passes = int.Parse((string)((ComboBoxItem)WipePasses.SelectedItem).Tag);
        _cts = new CancellationTokenSource();
        WipeBtn.IsEnabled = false;
        WipeCancel.Visibility = Visibility.Visible;
        var progress = new Progress<(int pass, double percent)>(p =>
        {
            WipeProgress.Value = p.percent;
            WipeStatus.Text = $"{Loc.T("Pasada")} {p.pass} de {passes}: {p.percent:0.0}%";
        });
        try
        {
            var ct = _cts.Token;
            await Task.Run(() => SecureWipe.WipeFreeSpace(drive, passes, progress, ct), ct);
            WipeStatus.Text = Loc.T("Espacio libre borrado correctamente.");
            WipeProgress.Value = 100;
        }
        catch (OperationCanceledException) { WipeStatus.Text = Loc.T("Cancelado. Se liberó el espacio temporal."); }
        catch (Exception ex) { WipeStatus.Text = Loc.T("Error: ") + ex.Message; }
        finally
        {
            WipeBtn.IsEnabled = true;
            WipeCancel.Visibility = Visibility.Collapsed;
        }
    }

    private void WipeCancel_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();
}
