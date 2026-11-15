using System.Windows;
using OpenSweep.Core;

namespace OpenSweep.Views;

/// <summary>
/// Diálogos modernos de OpenSweep. Dialogs.Show tiene la misma forma que MessageBox.Show,
/// así que se puede usar como reemplazo directo.
/// </summary>
public static class Dialogs
{
    private static Window? Owner() =>
        Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive && w is not ModernDialog)
        ?? (MainWindow.Current is { IsVisible: true } m ? m : null);

    /// <summary>Separa título y detalle: «Título\n\nDetalle» o la primera frase como título.</summary>
    private static (string title, string? body) Split(string text, string caption)
    {
        var parts = text.Split("\n\n", 2);
        if (parts.Length == 2) return (parts[0].Trim(), parts[1].Trim());
        if (!string.IsNullOrWhiteSpace(caption) && caption != "OpenSweep") return (caption, text);
        if (text.Length > 70)
        {
            foreach (var sep in new[] { "? ", ". ", ":\n", "\n" })
            {
                var i = text.IndexOf(sep, StringComparison.Ordinal);
                if (i > 10 && i < 120) return (text[..(i + 1)].Trim(), text[(i + sep.Length)..].Trim());
            }
        }
        return (text, null);
    }

    private static bool IsDestructive(string text)
    {
        var t = text.ToLowerInvariant();
        return t.Contains("elimin") || t.Contains("borr") || t.Contains("destru") || t.Contains("papelera") || t.Contains("desinstal")
               || t.Contains("delete") || t.Contains("shred") || t.Contains("recycle") || t.Contains("uninstall");
    }

    /// <summary>Texto del botón principal según la acción (más claro que «Sí»).</summary>
    private static string PrimaryVerb(string text)
    {
        var t = text.ToLowerInvariant();
        if (t.Contains("papelera") || t.Contains("recycle")) return Loc.T("Enviar a la papelera");
        if (t.Contains("destru") || t.Contains("shred")) return Loc.T("Destruir");
        if (t.Contains("desinstal") || t.Contains("uninstall")) return Loc.T("Desinstalar");
        if (t.Contains("elimin") || t.Contains("borr") || t.Contains("delete")) return Loc.T("Eliminar");
        if (t.Contains("restaur") || t.Contains("restore")) return Loc.T("Restaurar");
        if (t.Contains("reinici") || t.Contains("restart")) return Loc.T("Reiniciar");
        return Loc.T("Sí");
    }

    public static MessageBoxResult Show(string text, string caption = "OpenSweep",
        MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage image = MessageBoxImage.None)
    {
        var (title, body) = Split(text, caption);
        bool destructive = buttons != MessageBoxButton.OK && IsDestructive(text);
        var kind = image switch
        {
            MessageBoxImage.Error => DialogKind.Error,
            MessageBoxImage.Warning when destructive => DialogKind.Danger,
            MessageBoxImage.Warning => DialogKind.Warning,
            MessageBoxImage.Question when destructive => DialogKind.Danger,
            MessageBoxImage.Question => DialogKind.Question,
            _ => destructive ? DialogKind.Danger : DialogKind.Info,
        };
        var d = new ModernDialog();
        d.SetKind(kind);
        d.SetText(title, body);
        switch (buttons)
        {
            case MessageBoxButton.OK:
                d.AddButton(Loc.T("Aceptar"), MessageBoxResult.OK, primary: true, isCancel: true);
                break;
            case MessageBoxButton.OKCancel:
                d.AddButton(Loc.T("Cancelar"), MessageBoxResult.Cancel, isCancel: true);
                d.AddButton(destructive ? PrimaryVerb(text) : Loc.T("Aceptar"), MessageBoxResult.OK, primary: true, danger: destructive);
                break;
            case MessageBoxButton.YesNo:
                d.AddButton(destructive ? Loc.T("Cancelar") : Loc.T("No"), MessageBoxResult.No, isCancel: true);
                d.AddButton(PrimaryVerb(text), MessageBoxResult.Yes, primary: true, danger: destructive);
                break;
            case MessageBoxButton.YesNoCancel:
                d.AddButton(Loc.T("Cancelar"), MessageBoxResult.Cancel, isCancel: true);
                d.AddButton(Loc.T("No"), MessageBoxResult.No);
                d.AddButton(Loc.T("Sí"), MessageBoxResult.Yes, primary: true);
                break;
        }
        var owner = Owner();
        if (owner != null) d.Owner = owner; else d.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        d.ShowDialog();
        return d.Result == MessageBoxResult.None
            ? (buttons is MessageBoxButton.YesNo ? MessageBoxResult.No : MessageBoxResult.Cancel)
            : d.Result;
    }

    /// <summary>Pide un texto al usuario. Devuelve null si cancela.</summary>
    public static string? Prompt(string title, string message, string initial = "")
    {
        var d = new ModernDialog();
        d.SetKind(DialogKind.Info);
        d.SetText(title, message);
        var box = d.AddInput(initial);
        d.AddButton(Loc.T("Cancelar"), MessageBoxResult.Cancel, isCancel: true);
        d.AddButton(Loc.T("Guardar"), MessageBoxResult.OK, primary: true).IsDefault = true;
        var owner = Owner();
        if (owner != null) d.Owner = owner; else d.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        d.ShowDialog();
        return d.Result == MessageBoxResult.OK ? box.Text.Trim() : null;
    }

    /// <summary>
    /// Ejecuta una tarea elemento a elemento mostrando una barra con porcentaje,
    /// y al terminar un resumen con una marca verde. Devuelve cuántos elementos tuvieron éxito.
    /// </summary>
    public static Task<int> RunAsync<T>(string title, IReadOnlyList<T> items, Func<T, bool> work,
        Func<T, string>? label, Func<int, int, string> summary)
    {
        int ok = 0, done = 0;
        var d = new ModernDialog { CanClose = false };
        d.SetKind(DialogKind.Info);
        d.SetText(title, null);
        d.ShowProgress(true);
        d.SetProgress(0, items.Count, null);
        var owner = Owner();
        if (owner != null) d.Owner = owner; else d.WindowStartupLocation = WindowStartupLocation.CenterScreen;

        d.Loaded += async (_, _) =>
        {
            await Task.Run(() =>
            {
                foreach (var item in items)
                {
                    bool success;
                    try { success = work(item); } catch { success = false; }
                    if (success) ok++;
                    done++;
                    var name = label?.Invoke(item);
                    d.Dispatcher.BeginInvoke(() => d.SetProgress(done, items.Count, name));
                }
            });
            var text = summary(ok, items.Count - ok);
            d.SetKind(ok > 0 || items.Count == 0 ? DialogKind.Success : DialogKind.Error);
            d.SetText(text, null);
            d.ShowProgress(false);
            d.ClearButtons();
            d.AddButton(Loc.T("Listo"), MessageBoxResult.OK, primary: true, isCancel: true);
            d.CanClose = true;
            Toast(text);
        };
        d.ShowDialog();
        return Task.FromResult(ok);
    }

    /// <summary>Aviso breve en la parte inferior de la ventana principal.</summary>
    public static void Toast(string text) => MainWindow.Current?.ShowToast(text);
}
