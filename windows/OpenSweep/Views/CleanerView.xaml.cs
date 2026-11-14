using System.Windows;
using System.Windows.Controls;
using OpenSweep.Core;
using OpenSweep.Services;

namespace OpenSweep.Views;

public sealed record SectionNode(string Name, List<CleanRule> Rules);
public sealed record GroupNode(string Name, List<SectionNode> Sections);

public partial class CleanerView : UserControl
{
    private List<CleanRule> _rules = [];
    private CancellationTokenSource? _cts;

    public CleanerView()
    {
        InitializeComponent();
        LoadRules();
    }

    private void LoadRules()
    {
        _rules = JunkCleaner.BuildRules();
        GroupsList.ItemsSource = _rules
            .GroupBy(r => r.Group)
            .Select(g => new GroupNode(Loc.T(g.Key), g.GroupBy(r => r.Section).Select(s => new SectionNode(Loc.T(s.Key), s.ToList())).ToList()))
            .ToList();
    }

    private void RefreshChecks()
    {
        var src = GroupsList.ItemsSource;
        GroupsList.ItemsSource = null;
        GroupsList.ItemsSource = src;
        SaveSelection();
    }

    private void SaveSelection()
    {
        var s = AppSettings.Current;
        foreach (var r in _rules) s.CleanerSelection[r.Id] = r.Selected;
        s.Save();
    }

    private void Rule_Changed(object sender, RoutedEventArgs e)
    {
        // Solo avisar cuando el cambio lo hace el usuario (no al refrescar la lista)
        if (sender is CheckBox { DataContext: CleanRule r, IsChecked: true } cb && r.Warning != null && (cb.IsMouseOver || cb.IsKeyboardFocused))
            MessageBox.Show(r.DisplayWarning, $"{r.DisplaySection} - {r.DisplayName}", MessageBoxButton.OK, MessageBoxImage.Information);
        SaveSelection();
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e) { _rules.ForEach(r => r.Selected = true); RefreshChecks(); }
    private void SelectNone_Click(object sender, RoutedEventArgs e) { _rules.ForEach(r => r.Selected = false); RefreshChecks(); }
    private void SelectDefault_Click(object sender, RoutedEventArgs e) { _rules.ForEach(r => r.Selected = r.DefaultOn); RefreshChecks(); }

    private void SetBusy(bool busy)
    {
        AnalyzeBtn.IsEnabled = CleanBtn.IsEnabled = GroupsList.IsEnabled = !busy;
        CancelBtn.Visibility = Progress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void Analyze_Click(object sender, RoutedEventArgs e) => await Run(clean: false);

    private async void Clean_Click(object sender, RoutedEventArgs e)
    {
        var selected = _rules.Where(r => r.Selected).ToList();
        if (selected.Count == 0) return;
        if (MessageBox.Show(Loc.T("Se eliminarán de forma permanente los archivos seleccionados. ¿Continuar?"), "OpenSweep",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        await Run(clean: true);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

    private async Task Run(bool clean)
    {
        var selected = _rules.Where(r => r.Selected).ToList();
        if (selected.Count == 0)
        {
            SummaryTitle.Text = Loc.T("Nada seleccionado");
            return;
        }

        // Programas abiertos que bloquean archivos
        var skip = new HashSet<CleanRule>();
        if (clean)
        {
            var running = selected.Where(r => r.Processes.Length > 0 && Util.IsProcessRunning(r.Processes)).ToList();
            if (running.Count > 0)
            {
                var names = string.Join(", ", running.Select(r => r.DisplaySection).Distinct());
                bool close = AppSettings.Current.CloseBrowsersAutomatically;
                if (!close)
                {
                    var answer = MessageBox.Show($"{Loc.T("Estos programas están abiertos")}: {names}.\n\n{Loc.T("¿Quieres cerrarlos para limpiarlos?")}\n\nSí = cerrarlos · No = omitirlos",
                        Loc.T("Programas abiertos"), MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
                    if (answer == MessageBoxResult.Cancel) return;
                    close = answer == MessageBoxResult.Yes;
                }
                if (close) Util.KillProcesses(running.SelectMany(r => r.Processes).Distinct().ToArray());
                else foreach (var r in running) skip.Add(r);
            }
        }

        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        SetBusy(true);
        SummaryTitle.Text = clean ? Loc.T("Limpiando...") : Loc.T("Analizando...");
        var results = new List<RuleResult>();
        Progress.Value = 0;
        try
        {
            int i = 0;
            foreach (var rule in selected)
            {
                if (skip.Contains(rule)) continue;
                SummaryText.Text = $"{rule.DisplaySection} - {rule.DisplayName}";
                var res = await Task.Run(() => clean ? JunkCleaner.Clean(rule, ct) : JunkCleaner.Analyze(rule, false, ct), ct);
                if (res.Count > 0 || res.Bytes > 0) results.Add(res);
                Progress.Value = ++i * 100.0 / selected.Count;
                ResultsGrid.ItemsSource = results.ToList();
            }
            long total = results.Sum(r => r.Bytes);
            long files = results.Where(r => r.Rule.CustomMeasure == null).Sum(r => r.Count);
            long failed = results.Sum(r => r.Failed);
            SummaryTitle.Text = clean ? $"{Loc.T("Limpieza completada")}: {Util.FormatBytes(total)} {Loc.T("liberados")}" : $"{Loc.T("Se pueden liberar")} {Util.FormatBytes(total)}";
            SummaryText.Text = clean
                ? $"{files:N0} {Loc.T("archivos eliminados.")}" + (failed > 0 ? $" {failed:N0} {Loc.T("archivos en uso no se pudieron eliminar.")}" : "") + (skip.Count > 0 ? Loc.T(" Se omitieron programas abiertos.") : "")
                : $"{files:N0} {Loc.T("archivos encontrados. Pulsa «Ejecutar limpieza» para eliminarlos.")}";
        }
        catch (OperationCanceledException)
        {
            SummaryTitle.Text = Loc.T("Operación cancelada");
            SummaryText.Text = "";
        }
        finally
        {
            SetBusy(false);
        }
    }
}
