using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using OpenSweep.Core;
using OpenSweep.Services;

namespace OpenSweep.Views;

public sealed record DriveRow(string Label, string Detail, double Percent);

public partial class HealthView : UserControl
{
    private List<CleanRule> _junkRules = [];
    private List<RegistryIssue> _regIssues = [];

    public HealthView()
    {
        InitializeComponent();
        LoadSystemInfo();
        Loaded += async (_, _) =>
        {
            WindowsRepair.Changed += OnRepairChanged;
            ShowRepair();
            var disks = await Task.Run(DiskHealth.Read);
            DiskList.ItemsSource = disks;
            DiskStatus.Text = disks.Count == 0 ? Loc.T("No se pudo leer el estado de los discos.")
                : disks.Any(d => d.State is DiskState.Bad or DiskState.Warning) ? Loc.T("Revisa los avisos de abajo.")
                : Loc.T("Datos que informa cada disco sobre su propio estado.");
        };
        Unloaded += (_, _) => WindowsRepair.Changed -= OnRepairChanged;
    }

    // ---------- Reparar Windows ----------
    private void OnRepairChanged() => Dispatcher.BeginInvoke(ShowRepair);

    private void ShowRepair()
    {
        RepairBtn.IsEnabled = !WindowsRepair.Running;
        RepairPanel.Visibility = WindowsRepair.Running ? Visibility.Visible : Visibility.Collapsed;
        RepairStep.Text = WindowsRepair.StepText;
        RepairPercent.Text = $"{WindowsRepair.Percent:0} %";
        RepairBar.Value = WindowsRepair.Percent;
        RepairBar.IsIndeterminate = WindowsRepair.Running && WindowsRepair.Percent <= 0;
        if (WindowsRepair.Result != null)
        {
            RepairResult.Visibility = Visibility.Visible;
            RepairResult.Text = WindowsRepair.Result;
            RepairResult.Foreground = (System.Windows.Media.Brush)FindResource(WindowsRepair.Success == true ? "AccentBrush" : "TextBrush");
        }
    }

    private async void Repair_Click(object sender, RoutedEventArgs e)
    {
        if (Dialogs.Show(Loc.T("La reparación tarda entre 10 y 30 minutos. Puedes seguir usando el PC y OpenSweep, pero no lo cierres hasta que termine.") + "\n\n" + Loc.T("¿Empezar ahora?"),
                "OpenSweep", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        RepairResult.Visibility = Visibility.Collapsed;
        await Task.Run(WindowsRepair.RunAsync);
        Dialogs.Toast(Loc.T("Reparación de Windows terminada"));
    }

    private void LoadSystemInfo()
    {
        try
        {
            using var nt = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            var product = nt?.GetValue("ProductName") as string ?? "Windows";
            var build = Environment.OSVersion.Version.Build;
            if (build >= 22000) product = product.Replace("Windows 10", "Windows 11");
            var display = nt?.GetValue("DisplayVersion") as string ?? "";
            OsText.Text = $"{product} {display} ({Loc.T("compilación")} {build})";
            using var cpu = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            CpuText.Text = $"Procesador: {(cpu?.GetValue("ProcessorNameString") as string ?? "").Trim()} · {Environment.ProcessorCount} hilos";
        }
        catch { }
        var (total, avail, load) = Native.GetMemory();
        RamText.Text = $"{Loc.T("Memoria")}: {Util.FormatBytes((long)total)} · {Loc.T("en uso")} {load}% (libre {Util.FormatBytes((long)avail)})";
        var up = TimeSpan.FromMilliseconds(Environment.TickCount64);
        UptimeText.Text = $"{Loc.T("Encendido desde hace")} {(int)up.TotalDays} d {up.Hours} h {up.Minutes} {Loc.T("min")}";

        DrivesList.ItemsSource = DriveInfo.GetDrives()
            .Where(d => d.IsReady && d.DriveType is DriveType.Fixed or DriveType.Removable)
            .Select(d => new DriveRow(
                $"{d.Name.TrimEnd('\\')} {d.VolumeLabel}",
                $"{Util.FormatBytes(d.TotalFreeSpace)} {Loc.T("libres de")} {Util.FormatBytes(d.TotalSize)}",
                d.TotalSize == 0 ? 0 : (d.TotalSize - d.TotalFreeSpace) * 100.0 / d.TotalSize))
            .ToList();
    }

    private async void Scan_Click(object sender, RoutedEventArgs e)
    {
        ScanBtn.IsEnabled = FixBtn.IsEnabled = false;
        Progress.Visibility = Visibility.Visible;
        ScoreTitle.Text = Loc.T("Analizando...");
        ScoreSub.Text = Loc.T("Esto puede tardar unos segundos.");
        long junk = 0; int startup = 0;
        await Task.Run(() =>
        {
            _junkRules = JunkCleaner.BuildRules().Where(r => r.Selected && r.Group != "Privacidad").ToList();
            foreach (var r in _junkRules)
            {
                try { junk += JunkCleaner.Analyze(r, false, CancellationToken.None).Bytes; } catch { }
            }
            _regIssues = RegistryCleaner.Scan(RegistryCleaner.Categories().Where(c => c.DefaultOn).Select(c => c.Name), null, CancellationToken.None);
            startup = StartupService.GetEntries(false, false).Count(s => s.Enabled);
        });
        JunkText.Text = Util.FormatBytes(junk);
        RegText.Text = $"{_regIssues.Count} {Loc.T("problemas")}";
        StartupText.Text = $"{startup} {Loc.T("habilitados")}";

        int problems = (junk > 500L * 1024 * 1024 ? 1 : 0) + (_regIssues.Count > 20 ? 1 : 0) + (startup > 10 ? 1 : 0);
        ScoreTitle.Text = problems switch
        {
            0 => Loc.T("Tu PC está en buen estado"),
            1 => Loc.T("Tu PC necesita un poco de atención"),
            _ => Loc.T("Tu PC necesita atención")
        };
        ScoreSub.Text = $"{Loc.T("Se pueden liberar")} {Util.FormatBytes(junk)} {Loc.T("y corregir")} {_regIssues.Count} {Loc.T("entradas del registro.")}";
        Progress.Visibility = Visibility.Collapsed;
        ScanBtn.IsEnabled = true;
        FixBtn.IsEnabled = junk > 0 || _regIssues.Count > 0;
    }

    private async void Fix_Click(object sender, RoutedEventArgs e)
    {
        var browsersOpen = _junkRules.SelectMany(r => r.Processes).Distinct().Where(p => Util.IsProcessRunning(p)).ToList();
        if (browsersOpen.Count > 0)
            Dialogs.Show(Loc.T("Algunos programas están abiertos (") + string.Join(", ", browsersOpen) + "). Sus archivos se omitirán; ciérralos para una limpieza completa.",
                "OpenSweep", MessageBoxButton.OK, MessageBoxImage.Information);

        ScanBtn.IsEnabled = FixBtn.IsEnabled = false;
        Progress.Visibility = Visibility.Visible;
        ScoreTitle.Text = Loc.T("Mejorando...");
        long freed = 0; int regFixed = 0;
        await Task.Run(() =>
        {
            foreach (var r in _junkRules.Where(r => r.Processes.Length == 0 || !Util.IsProcessRunning(r.Processes)))
            {
                try { freed += JunkCleaner.Clean(r, CancellationToken.None).Bytes; } catch { }
            }
            if (_regIssues.Count > 0)
            {
                // En la reparación de un clic siempre se guarda copia; si no se puede, el registro no se toca
                if (RegistryCleaner.Backup(_regIssues) is { } backup && File.Exists(backup))
                    regFixed = RegistryCleaner.Fix(_regIssues).fixedCount;
            }
        });
        Progress.Visibility = Visibility.Collapsed;
        ScoreTitle.Text = Loc.T("¡Listo!");
        ScoreSub.Text = $"{Loc.T("Se liberaron")} {Util.FormatBytes(freed)} {Loc.T("y se corrigieron")} {regFixed} {Loc.T("entradas del registro.")}";
        JunkText.Text = "0 B"; RegText.Text = Loc.T("0 problemas");
        Dialogs.Toast(ScoreSub.Text);
        ScanBtn.IsEnabled = true;
        LoadSystemInfo();
    }

    private void Startup_Click(object sender, RoutedEventArgs e) => MainWindow.Current?.Navigate<StartupView>();
}
