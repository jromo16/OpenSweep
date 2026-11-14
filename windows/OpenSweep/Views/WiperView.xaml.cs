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
        var dlg = new OpenFileDialog { Multiselect = true, Title = "Archivos a destruir" };
        if (dlg.ShowDialog() == true) foreach (var f in dlg.FileNames) if (!_targets.Contains(f)) _targets.Add(f);
    }

    private void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Carpeta a destruir (con todo su contenido)" };
        if (dlg.ShowDialog() != true) return;
        if (PathHelper.IsProtectedFolder(dlg.FolderName))
        {
            MessageBox.Show("Esa carpeta es del sistema y no se puede destruir.", "OpenSweep", MessageBoxButton.OK, MessageBoxImage.Warning);
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
        if (MessageBox.Show($"Se destruirán {_targets.Count} elementos de forma IRRECUPERABLE.\n\n¿Estás seguro?", "Destruir archivos",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        int passes = int.Parse((string)((ComboBoxItem)ShredPasses.SelectedItem).Tag);
        ShredBtn.IsEnabled = false;
        var targets = _targets.ToList();
        var progress = new Progress<string>(s => ShredStatus.Text = s);
        var (ok, fail) = await Task.Run(() =>
        {
            int ok = 0, fail = 0;
            foreach (var t in targets)
            {
                var files = Directory.Exists(t) ? Util.SafeFiles(t).Select(f => f.FullName).ToList() : [t];
                foreach (var f in files)
                {
                    ((IProgress<string>)progress).Report("Destruyendo " + f);
                    if (SecureWipe.ShredFile(f, passes)) ok++; else fail++;
                }
                if (Directory.Exists(t)) Util.RemoveEmptyDirs(t, removeRoot: true);
            }
            return (ok, fail);
        });
        _targets.Clear();
        ShredBtn.IsEnabled = true;
        ShredStatus.Text = $"{ok} archivos destruidos." + (fail > 0 ? $" {fail} no se pudieron destruir (en uso o sin permisos)." : "");
    }

    private async void Wipe_Click(object sender, RoutedEventArgs e)
    {
        if (DriveBox.SelectedItem is not string drive) return;
        if (MessageBox.Show($"Se llenará el espacio libre de {drive} temporalmente. Cierra otros programas y no copies archivos mientras tanto.\n\n¿Continuar?",
                "Borrar espacio libre", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        int passes = int.Parse((string)((ComboBoxItem)WipePasses.SelectedItem).Tag);
        _cts = new CancellationTokenSource();
        WipeBtn.IsEnabled = false;
        WipeCancel.Visibility = Visibility.Visible;
        var progress = new Progress<(int pass, double percent)>(p =>
        {
            WipeProgress.Value = p.percent;
            WipeStatus.Text = $"Pasada {p.pass} de {passes}: {p.percent:0.0}%";
        });
        try
        {
            var ct = _cts.Token;
            await Task.Run(() => SecureWipe.WipeFreeSpace(drive, passes, progress, ct), ct);
            WipeStatus.Text = "Espacio libre borrado correctamente.";
            WipeProgress.Value = 100;
        }
        catch (OperationCanceledException) { WipeStatus.Text = "Cancelado. Se liberó el espacio temporal."; }
        catch (Exception ex) { WipeStatus.Text = "Error: " + ex.Message; }
        finally
        {
            WipeBtn.IsEnabled = true;
            WipeCancel.Visibility = Visibility.Collapsed;
        }
    }

    private void WipeCancel_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();
}
