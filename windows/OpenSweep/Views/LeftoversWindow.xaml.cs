using System.Text;
using System.Windows;
using System.Windows.Controls;
using OpenSweep.Core;
using OpenSweep.Services;

namespace OpenSweep.Views;

/// <summary>
/// Asistente de desinstalación (como el de Revo, en pasos): punto de restauración y desinstalador oficial →
/// modo de análisis → restos en el registro → archivos y carpetas → resumen.
/// Los archivos van a la Papelera de reciclaje y el registro se copia a un .reg antes de borrarlo.
/// </summary>
public partial class LeftoversWindow : Window
{
    private enum Step { Work, Mode, Registry, Files, Summary }

    private readonly InstalledProgram _program;
    private readonly bool _runUninstaller;
    private readonly bool _preset;
    private List<Leftover> _items = [];
    private readonly List<Leftover> _deleted = [];
    private Step _step = Step.Work;
    private bool _busy;
    private bool _stillInstalled;
    private string? _backupNote;
    private UninstallRecord? _record;

    /// <summary>Muestra una lista ya conocida (monitor de instalaciones, desinstalación por lotes) sin buscar.</summary>
    public LeftoversWindow(string title, List<Leftover> items) : this(UninstallerService.FakeProgram(title, null), forced: true)
    {
        _preset = true;
        _items = items;
    }

    /// <param name="forced">Desinstalación forzada: no se ejecuta ningún desinstalador, se buscan los restos directamente.</param>
    /// <param name="runUninstaller">Crear el punto de restauración y ejecutar el desinstalador oficial dentro del asistente.</param>
    public LeftoversWindow(InstalledProgram program, bool forced, bool runUninstaller = false)
    {
        InitializeComponent();
        WindowFit.FitToWorkArea(this, 900, 640, 640, 460);
        _program = program;
        _runUninstaller = runUninstaller && !forced;
        Title = $"{(forced ? Loc.T("Desinstalación forzada") : Loc.T("Desinstalando"))} {program.Name}";
        Loaded += async (_, _) => await Start();
        Closing += (_, e) =>
        {
            if (_busy && Dialogs.Show(Loc.T("Hay una operación en curso. ¿Cerrar de todos modos?"), "OpenSweep",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) e.Cancel = true;
        };
        Mode_Changed(this, new RoutedEventArgs());
    }

    private async Task Start()
    {
        if (_preset) { Go(_items.Any(i => i.IsRegistry || i.Special != null) ? Step.Registry : Step.Files); return; }
        if (!_runUninstaller) { Go(Step.Mode); return; }

        _busy = true;
        Go(Step.Work);
        try
        {
            if (AppSettings.Current.CreateRestorePointBeforeUninstall)
            {
                ShowWork(Loc.T("Creando un punto de restauración del sistema…"), Loc.T("Si algo sale mal, podrás volver atrás desde «Copias de seguridad»."));
                await Task.Run(() => UninstallerService.CreateRestorePoint($"{Loc.T("OpenSweep: antes de desinstalar")} {_program.Name}"));
            }
            ShowWork($"{Loc.T("Ejecutando el desinstalador de")} {_program.Name}…", Loc.T("Sigue los pasos de su ventana. Cuando termine, OpenSweep buscará lo que haya quedado."));
            try { await UninstallerService.RunUninstallerAsync(_program, quiet: false); }
            catch (Exception ex)
            {
                Dialogs.Show($"{Loc.T("No se pudo ejecutar el desinstalador")}:\n{ex.Message}\n\n{Loc.T("Puedes buscar los restos igualmente (desinstalación forzada).")}",
                    "OpenSweep", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            _stillInstalled = UninstallerService.StillInstalled(_program);
        }
        finally { _busy = false; }
        Go(Step.Mode);
    }

    // ---------------- Navegación ----------------
    private void Go(Step step)
    {
        _step = step;
        PageWork.Visibility = step == Step.Work ? Visibility.Visible : Visibility.Collapsed;
        PageMode.Visibility = step == Step.Mode ? Visibility.Visible : Visibility.Collapsed;
        PageTree.Visibility = step is Step.Registry or Step.Files ? Visibility.Visible : Visibility.Collapsed;
        PageSummary.Visibility = step == Step.Summary ? Visibility.Visible : Visibility.Collapsed;

        var names = new[] { Loc.T("Desinstalar"), Loc.T("Analizar"), Loc.T("Registro"), Loc.T("Archivos"), Loc.T("Resumen") };
        StepsText.Text = string.Join("   ›   ", names.Select((n, i) => i == (int)step ? $"● {n}" : n));
        StepTitle.Text = step switch
        {
            Step.Work => Loc.T("Desinstalación"),
            Step.Mode => Loc.T("Buscar restos"),
            Step.Registry => Loc.T("Restos en el registro"),
            Step.Files => Loc.T("Archivos y carpetas que quedaron"),
            _ => Loc.T("Resumen"),
        };

        BackBtn.Visibility = step is Step.Registry or Step.Files && !(_preset && step == Step.Registry) ? Visibility.Visible : Visibility.Collapsed;
        BackBtn.IsEnabled = !(_preset && step == Step.Files && !_items.Any(i => i.IsRegistry || i.Special != null));
        NextBtn.IsEnabled = step != Step.Work;
        NextBtn.Content = step switch
        {
            Step.Mode => Loc.T("Analizar"),
            Step.Files => Loc.T("Finalizar"),
            Step.Summary => Loc.T("Cerrar"),
            _ => Loc.T("Siguiente"),
        };
        CancelBtn.Visibility = step == Step.Summary ? Visibility.Collapsed : Visibility.Visible;

        if (step == Step.Mode)
        {
            ModeIntro.Text = _runUninstaller
                ? Loc.T("El desinstalador terminó. Elige cuánto buscar y pulsa «Analizar» para encontrar los archivos, carpetas y entradas del registro que hayan quedado.")
                : $"{Loc.T("Se buscarán los archivos, carpetas y entradas del registro de")} {_program.Name}.";
            StillInstalledText.Visibility = _stillInstalled ? Visibility.Visible : Visibility.Collapsed;
        }
        if (step is Step.Registry or Step.Files) ShowTree();
        if (step == Step.Summary) ShowSummary();
    }

    private void ShowWork(string text, string sub) { WorkText.Text = text; WorkSub.Text = sub; }

    private async void Next_Click(object sender, RoutedEventArgs e)
    {
        switch (_step)
        {
            case Step.Mode: await Analyze(); break;
            case Step.Registry:
                if (!ConfirmSkip()) return;
                Go(Step.Files); break;
            case Step.Files:
                if (!ConfirmSkip()) return;
                Go(Step.Summary); break;
            case Step.Summary: Close(); break;
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (_step == Step.Files && _items.Any(i => i.IsRegistry || i.Special != null)) Go(Step.Registry);
        else if (!_preset) Go(Step.Mode);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>Como en Revo: si quedan elementos marcados sin eliminar, se pregunta antes de seguir.</summary>
    private bool ConfirmSkip()
    {
        var marked = PageItems().Count(i => i.Selected);
        if (marked == 0) return true;
        return Dialogs.Show($"{Loc.T("Hay")} {marked} {Loc.T("elementos marcados que todavía no has eliminado.")}\n\n{Loc.T("¿Continuar sin eliminarlos?")}",
            "OpenSweep", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
    }

    // ---------------- Análisis ----------------
    private ScanMode Mode => ModeSafe.IsChecked == true ? ScanMode.Seguro : ModeAdvanced.IsChecked == true ? ScanMode.Avanzado : ScanMode.Moderado;

    private void Mode_Changed(object sender, RoutedEventArgs e)
    {
        if (ModeDescription == null) return;
        ModeDescription.Text = Mode switch
        {
            ScanMode.Seguro => Loc.T("Seguro: solo las carpetas y claves del registro con el nombre exacto del programa. Lo más rápido."),
            ScanMode.Avanzado => Loc.T("Avanzado: además busca nombres parecidos. Lo que solo coincide en parte aparece sin marcar para que lo revises."),
            _ => Loc.T("Moderado: incluye el modo Seguro y además carpetas del fabricante, servicios, tareas programadas, reglas del firewall, menús contextuales, temporales y los rastros que deja Windows (compatibilidad y Prefetch)."),
        };
    }

    private async Task Analyze()
    {
        _busy = true;
        Go(Step.Work);
        ShowWork(Loc.T("Buscando restos…"), Loc.T("Revisando carpetas, registro, servicios y tareas programadas."));
        var mode = Mode;
        try
        {
            _items = await Task.Run(() =>
            {
                var found = UninstallerService.FindLeftovers(_program, mode, CancellationToken.None);
                // Si se registró su instalación, se añade todo lo que creó el instalador y todavía existe
                var trace = InstallMonitor.TraceFor(_program);
                if (trace != null)
                {
                    var seen = found.Select(i => i.Kind + "|" + i.Display).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    var extras = new List<Leftover>();
                    foreach (var extra in InstallMonitor.RemainingItems(trace))
                    {
                        if (found.Any(f => !f.IsRegistry && f.IsDirectory && extra.Path.StartsWith(f.Path + "\\", StringComparison.OrdinalIgnoreCase))) continue;
                        if (!seen.Add(extra.Kind + "|" + extra.Display)) continue;
                        extra.Reason = Loc.T("Creado por el instalador (registro de instalación)");
                        extras.Add(extra);
                    }
                    // Mientras se instalaba pudo cambiar otro programa: solo se marca lo que es claramente de este
                    UninstallerService.ReviewTraceItems(_program, extras);
                    found.AddRange(extras);
                }
                return found;
            });
        }
        catch (Exception ex)
        {
            // Si algo falla, no se queda colgado en «Buscando…»: se avisa y se vuelve a elegir el modo
            _busy = false;
            Dialogs.Show($"{Loc.T("No se pudo terminar la búsqueda de restos")}:\n{ex.Message}", "OpenSweep", MessageBoxButton.OK, MessageBoxImage.Warning);
            Go(Step.Mode);
            return;
        }
        finally { _busy = false; }
        Go(_items.Any(i => i.IsRegistry || i.Special != null) || _items.Count == 0 ? Step.Registry : Step.Files);
    }

    // ---------------- Árboles ----------------
    private List<Leftover> PageItems() => _step == Step.Registry
        ? _items.Where(i => i.IsRegistry || i.Special != null).ToList()
        : _step == Step.Files ? _items.Where(i => !i.IsRegistry && i.Special == null).ToList() : [];

    private void ShowTree()
    {
        var items = PageItems();
        bool reg = _step == Step.Registry;
        Tree.ItemsSource = reg ? LeftoverTree.BuildRegistry(items) : LeftoverTree.BuildFiles(items);
        TreeIntro.Text = reg
            ? Loc.T("Claves y valores del registro que dejó el programa, y sus servicios, tareas y reglas del firewall. Lo marcado en naranja puede ser de otro programa: revísalo antes de marcarlo. Se guarda una copia .reg antes de borrar.")
            : Loc.T("Archivos y carpetas que quedaron. Se envían a la Papelera de reciclaje, así que puedes recuperarlos si hace falta.");
        TreeEmpty.Text = reg ? Loc.T("No se encontraron restos en el registro.") : Loc.T("No se encontraron archivos ni carpetas.");
        TreeEmpty.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        DeleteBtn.IsEnabled = items.Count > 0;
        TreeCounts.Text = reg
            ? $"{Loc.T("Claves")}: {items.Count(i => i.IsRegistry && i.ValueName == null)} · {Loc.T("Valores")}: {items.Count(i => i.ValueName != null)}"
              + (items.Any(i => i.Special != null) ? $" · {Loc.T("Otros")}: {items.Count(i => i.Special != null)}" : "")
            : $"{Loc.T("Carpetas")}: {items.Count(i => i.IsDirectory)} · {Loc.T("Archivos")}: {items.Count(i => !i.IsDirectory)} · {Util.FormatBytes(items.Sum(i => i.Size))}";
    }

    private void SetAll(bool value)
    {
        if (Tree.ItemsSource is not IEnumerable<LeftoverNode> roots) return;
        foreach (var r in roots) r.IsChecked = value;
    }

    private void CheckAll_Click(object sender, RoutedEventArgs e) => SetAll(true);
    private void UncheckAll_Click(object sender, RoutedEventArgs e) => SetAll(false);

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        var sel = PageItems().Where(i => i.Selected).ToList();
        if (sel.Count == 0) { Dialogs.Show(Loc.T("Marca primero los elementos que quieres eliminar."), "OpenSweep"); return; }
        var warn = sel.Count(i => i.Shared || i.ByNameOnly);
        var text = $"{Loc.T("Se eliminarán")} {sel.Count} {Loc.T("elementos.")}" +
                   (_step == Step.Registry ? "\n" + Loc.T("Antes se guarda una copia de seguridad del registro.") : "\n" + Loc.T("Irán a la Papelera de reciclaje.")) +
                   (warn > 0 ? $"\n\n⚠ {warn} {Loc.T("de ellos podrían ser de otro programa.")}" : "") +
                   "\n\n" + Loc.T("¿Continuar?");
        if (Dialogs.Show(text, Loc.T("Eliminar restos"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        _busy = true;
        DeleteBtn.IsEnabled = NextBtn.IsEnabled = BackBtn.IsEnabled = false;
        var removed = new List<Leftover>();
        _record ??= new UninstallRecord { Program = _program.Name };
        try
        {
            if (sel.Any(i => i.IsRegistry || i.Special != null))
            {
                var (regFile, tasks) = await Task.Run(() => UninstallerService.BackupRegistryLeftovers(sel));
                bool hasReg = sel.Any(i => i.IsRegistry || i.Special is "service" or "firewall");
                if (hasReg && !BackupLooksValid(regFile))
                {
                    // Sin copia no se podría deshacer: el usuario decide
                    if (Dialogs.Show(Loc.T("No se pudo guardar la copia de seguridad del registro, así que esto no se podrá deshacer.") + "\n\n" +
                                     Loc.T("¿Eliminar igualmente lo marcado del registro?"), Loc.T("Eliminar restos"),
                            MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                        sel = sel.Where(i => !i.IsRegistry && i.Special == null).ToList();
                }
                else if (regFile != null) _record.RegFiles.Add(regFile);
                _record.Tasks.AddRange(tasks.Select(t => new[] { t.task, t.xml }));
            }
            await Dialogs.RunAsync(Loc.T("Eliminando restos…"), sel,
                i =>
                {
                    if (!UninstallerService.DeleteLeftover(i)) return false;
                    lock (removed) removed.Add(i);
                    return true;
                },
                i => i.ValueName ?? System.IO.Path.GetFileName(i.Path.TrimEnd('\\')),
                (done, bad) => $"{done} {Loc.T("restos eliminados")}" + (bad > 0 ? $" · {bad} {Loc.T("no se pudieron eliminar (pueden estar en uso)")}" : ""));
        }
        catch (Exception ex)
        {
            Dialogs.Show(ex.Message, "OpenSweep", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _busy = false;
            NextBtn.IsEnabled = BackBtn.IsEnabled = true;
        }
        _deleted.AddRange(removed);
        // Registro para deshacer: lo que fue a la papelera y cuánto se quitó del registro
        _record.Recycled.AddRange(removed.Where(i => !i.IsRegistry && i.Special == null).Select(i => i.Path));
        _record.RegistryItems += removed.Count(i => i.IsRegistry && i.Special == null);
        _record.OtherItems += removed.Count(i => i.Special is "service" or "firewall");
        if (removed.Count > 0)
        {
            try { UninstallBackups.Save(_record); } catch { }
            _backupNote = Loc.T("Puedes deshacer esta limpieza desde «Copias de seguridad» › «Desinstalaciones».");
        }
        _items = _items.Except(removed).ToList();
        ShowTree();
    }

    /// <summary>¿El .reg existe y contiene al menos una clave (no solo la cabecera)?</summary>
    private static bool BackupLooksValid(string? regFile)
    {
        try { return regFile != null && File.Exists(regFile) && File.ReadAllText(regFile).Contains("[HKEY_"); }
        catch { return false; }
    }

    // ---------------- Resumen ----------------
    private void ShowSummary()
    {
        SummaryTitle.Text = _runUninstaller
            ? (_stillInstalled ? $"{_program.Name} {Loc.T("sigue instalado")}" : $"{Loc.T("Has desinstalado")} {_program.Name}")
            : $"{Loc.T("Limpieza de restos de")} {_program.Name} {Loc.T("terminada")}";
        SummaryLines.Children.Clear();
        void Line(string label, int n) => SummaryLines.Children.Add(new DockPanel
        {
            Margin = new Thickness(0, 3, 0, 3),
            Children =
            {
                new TextBlock { Text = n.ToString(), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 4, 0) }.Also(t => DockPanel.SetDock(t, Dock.Right)),
                new TextBlock { Text = label },
            },
        });
        Line(Loc.T("Claves del registro eliminadas"), _deleted.Count(i => i.IsRegistry && i.ValueName == null && i.Special == null));
        Line(Loc.T("Valores del registro eliminados"), _deleted.Count(i => i.ValueName != null));
        Line(Loc.T("Servicios, tareas y reglas eliminados"), _deleted.Count(i => i.Special != null));
        Line(Loc.T("Carpetas enviadas a la papelera"), _deleted.Count(i => !i.IsRegistry && i.IsDirectory && i.Special == null));
        Line(Loc.T("Archivos enviados a la papelera"), _deleted.Count(i => !i.IsRegistry && !i.IsDirectory && i.Special == null));
        var left = _items.Count;
        if (left > 0) Line(Loc.T("Restos que se dejaron sin eliminar"), left);
        SummaryBackup.Text = _backupNote ?? "";
        var sb = new StringBuilder();
        foreach (var i in _deleted) sb.AppendLine($"{Loc.T("Eliminado")}: {i.Kind} · {i.Display}");
        foreach (var i in _items) sb.AppendLine($"{Loc.T("Se dejó")}: {i.Kind} · {i.Display}");
        DetailsText.Text = sb.Length == 0 ? Loc.T("No había nada que eliminar.") : sb.ToString();
        if (_deleted.Count > 0)
            Scheduler.Log($"Desinstalador: {_program.Name}, {_deleted.Count} restos eliminados");
    }
}

internal static class FluentExtensions
{
    public static T Also<T>(this T value, Action<T> action) { action(value); return value; }
}
