using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using OpenSweep.Core;
using OpenSweep.Services;

namespace OpenSweep.Views;

public partial class RegistryCleanerView : UserControl
{
    private readonly List<RegistryCategory> _categories = RegistryCleaner.Categories();
    private List<RegistryIssue> _issues = [];

    public RegistryCleanerView()
    {
        InitializeComponent();
        CategoriesList.ItemsSource = _categories;
    }

    private void Bind()
    {
        var view = new ListCollectionView(_issues);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(RegistryIssue.Category)));
        IssuesGrid.ItemsSource = view;
        FixBtn.IsEnabled = _issues.Count > 0;
    }

    private async void Scan_Click(object sender, RoutedEventArgs e)
    {
        ScanBtn.IsEnabled = FixBtn.IsEnabled = false;
        Progress.Visibility = Visibility.Visible;
        SummaryTitle.Text = "Buscando problemas...";
        var cats = _categories.Where(c => c.Selected).Select(c => c.Name).ToList();
        var progress = new Progress<string>(s => SummaryText.Text = s);
        _issues = await Task.Run(() => RegistryCleaner.Scan(cats, progress, CancellationToken.None));
        Bind();
        Progress.Visibility = Visibility.Collapsed;
        ScanBtn.IsEnabled = true;
        SummaryTitle.Text = _issues.Count == 0 ? "No se encontraron problemas" : $"Se encontraron {_issues.Count} problemas";
        SummaryText.Text = _issues.Count == 0 ? "El registro está limpio." : "Revisa la lista y pulsa «Reparar seleccionados».";
    }

    private async void Fix_Click(object sender, RoutedEventArgs e)
    {
        IssuesGrid.CommitEdit(DataGridEditingUnit.Row, true);
        var selected = _issues.Where(i => i.Selected).ToList();
        if (selected.Count == 0) return;

        string? backup = null;
        if (AppSettings.Current.BackupRegistryBeforeFix)
        {
            var ask = MessageBox.Show("¿Deseas guardar una copia de seguridad de los cambios en el registro?\n(Recomendado)", "OpenSweep",
                MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (ask == MessageBoxResult.Cancel) return;
            if (ask == MessageBoxResult.Yes)
            {
                backup = await Task.Run(() => RegistryCleaner.Backup(selected));
                if (backup == null &&
                    MessageBox.Show("No se pudo crear la copia de seguridad. ¿Continuar igualmente?", "OpenSweep", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                    return;
            }
        }

        ScanBtn.IsEnabled = FixBtn.IsEnabled = false;
        Progress.Visibility = Visibility.Visible;
        var (ok, failed) = await Task.Run(() => RegistryCleaner.Fix(selected));
        Progress.Visibility = Visibility.Collapsed;
        _issues = _issues.Except(selected).ToList();
        Bind();
        ScanBtn.IsEnabled = true;
        SummaryTitle.Text = $"Se repararon {ok} problemas";
        SummaryText.Text = (failed > 0 ? $"{failed} no se pudieron reparar. " : "") + (backup != null ? $"Copia guardada en: {backup}" : "");
    }

    private void CheckAll_Click(object sender, RoutedEventArgs e) { _issues.ForEach(i => i.Selected = true); Bind(); }
    private void UncheckAll_Click(object sender, RoutedEventArgs e) { _issues.ForEach(i => i.Selected = false); Bind(); }

    private void OpenBackups_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(AppSettings.BackupFolder);
        Util.OpenInExplorer(AppSettings.BackupFolder);
    }
}
