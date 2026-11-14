using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using OpenSweep.Core;
using OpenSweep.Services;

namespace OpenSweep.Views;

public sealed record RegBackup(string Path, DateTime Date, long Size)
{
    public string Name => System.IO.Path.GetFileName(Path);
    public string DateText => Date.ToString("dd/MM/yyyy HH:mm");
    public string SizeText => Util.FormatBytes(Size);
}

public sealed class RestorePointRow
{
    public int SequenceNumber { get; set; }
    public string Description { get; set; } = "";
    public string Date { get; set; } = "";
}

public partial class BackupsView : UserControl
{
    public BackupsView()
    {
        InitializeComponent();
        Loaded += async (_, _) => { LoadRegs(); await LoadPoints(); };
    }

    private void LoadRegs()
    {
        RegGrid.ItemsSource = Util.SafeFiles(AppSettings.BackupFolder, "*.reg", false)
            .Select(f => new RegBackup(f.FullName, f.LastWriteTime, f.Length))
            .OrderByDescending(b => b.Date).ToList();
    }

    private async Task LoadPoints()
    {
        PointsStatus.Text = Loc.T("Cargando puntos de restauración…");
        var (code, output) = await Task.Run(() => Util.PowerShell(
            "Get-ComputerRestorePoint | ForEach-Object { [pscustomobject]@{ SequenceNumber=[int]$_.SequenceNumber; Description=$_.Description; " +
            "Date=([Management.ManagementDateTimeConverter]::ToDateTime($_.CreationTime)).ToString('dd/MM/yyyy HH:mm') } } | ConvertTo-Json -Compress"));
        var list = new List<RestorePointRow>();
        try
        {
            var json = output.Trim();
            var start = json.IndexOfAny(['[', '{']);
            if (code == 0 && start >= 0)
            {
                json = json[start..];
                list = json.StartsWith('[')
                    ? JsonSerializer.Deserialize<List<RestorePointRow>>(json) ?? []
                    : [JsonSerializer.Deserialize<RestorePointRow>(json)!];
            }
        }
        catch { }
        PointsGrid.ItemsSource = list.OrderByDescending(p => p.SequenceNumber).ToList();
        PointsStatus.Text = list.Count == 0
            ? Loc.T("No hay puntos de restauración o la Protección del sistema está desactivada.")
            : $"{list.Count} {Loc.T("puntos de restauración")}";
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(AppSettings.BackupFolder);
        Util.OpenInExplorer(AppSettings.BackupFolder);
    }

    private void RestoreReg_Click(object sender, RoutedEventArgs e)
    {
        if (RegGrid.SelectedItem is not RegBackup b) return;
        if (Dialogs.Show($"{Loc.T("¿Restaurar en el registro el contenido de")} {b.Name}?", "OpenSweep",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        var (code, output) = Util.Run("reg.exe", $"import \"{b.Path}\"");
        Dialogs.Show(code == 0 ? Loc.T("Copia restaurada correctamente.") : Loc.T("No se pudo restaurar la copia:") + "\n" + output, "OpenSweep");
    }

    private void DeleteReg_Click(object sender, RoutedEventArgs e)
    {
        var sel = RegGrid.SelectedItems.OfType<RegBackup>().ToList();
        if (sel.Count == 0) return;
        if (Dialogs.Show($"{Loc.T("¿Eliminar")} {sel.Count} {Loc.T("copias de seguridad?")}", "OpenSweep", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        foreach (var b in sel) Util.TryDeleteFile(b.Path);
        LoadRegs();
    }

    private async void CreatePoint_Click(object sender, RoutedEventArgs e)
    {
        CreateBtn.IsEnabled = false;
        PointsStatus.Text = Loc.T("Creando punto de restauración…");
        bool ok = await Task.Run(() => UninstallerService.CreateRestorePoint("OpenSweep: " + Loc.T("punto manual")));
        CreateBtn.IsEnabled = true;
        if (!ok) Dialogs.Show(Loc.T("Windows no permitió crear el punto. Solo se permite uno cada 24 horas, o la Protección del sistema está desactivada."), "OpenSweep");
        await LoadPoints();
    }

    private void OpenRstrui_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("rstrui.exe") { UseShellExecute = true }); } catch { }
    }
}
