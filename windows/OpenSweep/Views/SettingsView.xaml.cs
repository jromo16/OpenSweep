using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using OpenSweep.Core;

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
        TempAge.SelectedItem = TempAge.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == _s.TempMinAgeHours.ToString()) ?? TempAge.Items[1];
        IncludeList.ItemsSource = _s.CustomFolders;
        ExcludeList.ItemsSource = _s.Exclusions;
        _loading = false;
    }

    private void Save(object? sender = null, RoutedEventArgs? e = null)
    {
        if (_loading) return;
        _s.SecureDeleteOnClean = SecureDelete.IsChecked == true;
        _s.CloseBrowsersAutomatically = CloseBrowsers.IsChecked == true;
        _s.CreateRestorePointBeforeUninstall = RestorePoint.IsChecked == true;
        _s.BackupRegistryBeforeFix = RegBackup.IsChecked == true;
        if (TempAge.SelectedItem is ComboBoxItem { Tag: string tag }) _s.TempMinAgeHours = int.Parse(tag);
        _s.Save();
    }

    private void TempAge_Changed(object sender, SelectionChangedEventArgs e) => Save();

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
            MessageBox.Show("No se puede incluir una carpeta del sistema o del perfil de usuario completa.", "OpenSweep", MessageBoxButton.OK, MessageBoxImage.Warning);
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
