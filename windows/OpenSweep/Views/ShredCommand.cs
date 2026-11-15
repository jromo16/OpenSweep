using System.Windows;
using OpenSweep.Core;

namespace OpenSweep.Views;

/// <summary>«Destruir con OpenSweep» desde el menú del clic derecho del Explorador: confirmación, progreso y resultado.</summary>
public static class ShredCommand
{
    public static async Task RunAsync(string path)
    {
        path = path.TrimEnd('\\');
        bool isDir = Directory.Exists(path);
        if (!isDir && !File.Exists(path)) return;
        if (isDir && PathHelper.IsProtectedFolder(path))
        {
            Dialogs.Show(Loc.T("Esa carpeta es del sistema y no se puede destruir."), "OpenSweep", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var files = isDir ? await Task.Run(() => Util.SafeFiles(path).Select(f => f.FullName).ToList()) : [path];
        var what = isDir ? $"{Loc.T("la carpeta")} «{Path.GetFileName(path)}» ({files.Count} {Loc.T("archivos")})" : $"«{Path.GetFileName(path)}»";
        if (Dialogs.Show($"{Loc.T("¿Destruir")} {what}?\n\n{Loc.T("Se sobrescribe antes de borrarse: no se podrá recuperar ni con programas de recuperación, y no pasa por la Papelera.")}",
                Loc.T("Destruir archivos"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        await Dialogs.RunAsync(Loc.T("Destruyendo archivos…"), files,
            f => SecureWipe.ShredFile(f, 1),
            f => Path.GetFileName(f),
            (done, bad) => $"{done} {Loc.T("archivos destruidos")}" + (bad > 0 ? $" · {bad} {Loc.T("no se pudieron destruir")}" : ""));
        if (isDir) Util.RemoveEmptyDirs(path, removeRoot: true);
        Services.Scheduler.Log($"Destruido desde el Explorador: {path}");
    }
}
