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
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(RegistryIssue.CategoryText)));
        IssuesGrid.ItemsSource = view;
        FixBtn.IsEnabled = _issues.Count > 0;
    }

    private async void Scan_Click(object sender, RoutedEventArgs e)
    {
        ScanBtn.IsEnabled = FixBtn.IsEnabled = false;
        Progress.Visibility = Visibility.Visible;
        SummaryTitle.Text = Loc.T("Buscando problemas...");
        var cats = _categories.Where(c => c.Selected).Select(c => c.Name).ToList();
        var progress = new Progress<string>(s => SummaryText.Text = s);
        _issues = await Task.Run(() => RegistryCleaner.Scan(cats, progress, CancellationToken.None));
        Bind();
        Progress.Visibility = Visibility.Collapsed;
        ScanBtn.IsEnabled = true;
        SummaryTitle.Text = _issues.Count == 0 ? Loc.T("No se encontraron problemas") : $"{Loc.T("Se encontraron")} {_issues.Count} {Loc.T("problemas")}";
        SummaryText.Text = _issues.Count == 0 ? Loc.T("El registro está limpio.") : Loc.T("Revisa la lista y pulsa «Reparar seleccionados».");
    }

    private async void Fix_Click(object sender, RoutedEventArgs e)
    {
        IssuesGrid.CommitEdit(DataGridEditingUnit.Row, true);
        var selected = _issues.Where(i => i.Selected).ToList();
        if (selected.Count == 0) return;

        string? backup = null;
        if (AppSettings.Current.BackupRegistryBeforeFix)
        {
            var ask = Dialogs.Show(Loc.T("¿Deseas guardar una copia de seguridad de los cambios en el registro?\n(Recomendado)"), "OpenSweep",
                MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (ask == MessageBoxResult.Cancel) return;
            if (ask == MessageBoxResult.Yes)
            {
                backup = await Task.Run(() => RegistryCleaner.Backup(selected));
                if (backup == null &&
                    Dialogs.Show(Loc.T("No se pudo crear la copia de seguridad. ¿Continuar igualmente?"), "OpenSweep", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                    return;
            }
        }

        ScanBtn.IsEnabled = FixBtn.IsEnabled = false;
        Progress.Visibility = Visibility.Visible;
        var fixedIssues = new List<RegistryIssue>();
        int ok = await Dialogs.RunAsync(Loc.T("Reparando el registro…"), selected,
            i =>
            {
                if (RegistryCleaner.Fix([i]).fixedCount == 0) return false;
                lock (fixedIssues) fixedIssues.Add(i);
                return true;
            },
            i => i.ProblemText,
            (done, bad) => $"{done} {Loc.T("problemas reparados")}" + (bad > 0 ? $" · {bad} {Loc.T("no se pudieron reparar")}" : ""));
        int failed = selected.Count - ok;
        Progress.Visibility = Visibility.Collapsed;
        _issues = _issues.Except(fixedIssues).ToList();
        Bind();
        ScanBtn.IsEnabled = true;
        SummaryTitle.Text = $"{Loc.T("Se repararon")} {ok} {Loc.T("problemas")}";
        SummaryText.Text = (failed > 0 ? $"{failed} {Loc.T("no se pudieron reparar.")} " : "") + (backup != null ? $"{Loc.T("Copia guardada en")}: {backup}" : "");
    }

    private void CheckAll_Click(object sender, RoutedEventArgs e) { _issues.ForEach(i => i.Selected = true); Bind(); }
    private void UncheckAll_Click(object sender, RoutedEventArgs e) { _issues.ForEach(i => i.Selected = false); Bind(); }

    private void OpenBackups_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(AppSettings.BackupFolder);
        Util.OpenInExplorer(AppSettings.BackupFolder);
    }
}
