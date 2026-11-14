using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using OpenSweep.Core;

namespace OpenSweep.Views;

public enum DialogKind { Info, Question, Warning, Danger, Error, Success }

/// <summary>Ventana de diálogo moderna (reemplaza a MessageBox).</summary>
public partial class ModernDialog : Window
{
    public MessageBoxResult Result { get; private set; } = MessageBoxResult.None;
    private MessageBoxResult _cancelResult = MessageBoxResult.Cancel;
    public bool CanClose { get; set; } = true;

    public ModernDialog()
    {
        InitializeComponent();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && CanClose && _cancelResult != MessageBoxResult.None) Finish(_cancelResult);
        };
        MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) try { DragMove(); } catch { } };
    }

    public void SetKind(DialogKind kind)
    {
        var (glyph, brushKey) = kind switch
        {
            DialogKind.Question => ("", "AccentBrush"),
            DialogKind.Warning => ("", "WarnBrush"),
            DialogKind.Danger => ("", "DangerBrush"),
            DialogKind.Error => ("", "DangerBrush"),
            DialogKind.Success => ("", "AccentBrush"),
            _ => ("", "AccentBrush"),
        };
        IconGlyph.Text = glyph;
        IconCircle.SetResourceReference(Border.BackgroundProperty, brushKey);
    }

    public void SetText(string title, string? message)
    {
        TitleText.Text = title;
        MessageText.Text = message ?? "";
        MessageText.Visibility = string.IsNullOrWhiteSpace(message) ? Visibility.Collapsed : Visibility.Visible;
    }

    public void ClearButtons() => ButtonsPanel.Children.Clear();

    /// <summary>Añade un botón. primary = verde (o rojo si danger).</summary>
    public Button AddButton(string text, MessageBoxResult result, bool primary = false, bool danger = false, bool isCancel = false)
    {
        var b = new Button { Content = text, MinWidth = 96, Padding = new Thickness(18, 7, 18, 7), Margin = new Thickness(8, 0, 0, 0) };
        if (primary && danger)
        {
            b.SetResourceReference(BackgroundProperty, "DangerBrush");
            b.Foreground = Brushes.White;
            b.BorderThickness = new Thickness(0);
        }
        else if (primary) b.SetResourceReference(StyleProperty, "AccentButtonStyle");
        if (primary) b.IsDefault = true;
        if (isCancel) _cancelResult = result;
        b.Click += (_, _) => Finish(result);
        ButtonsPanel.Children.Add(b);
        if (primary) Loaded += (_, _) => b.Focus();
        return b;
    }

    public void Finish(MessageBoxResult r)
    {
        Result = r;
        CanClose = true;
        try { DialogResult = r is MessageBoxResult.OK or MessageBoxResult.Yes; } catch { Close(); }
    }

    // ---------- Progreso ----------
    public void ShowProgress(bool show)
    {
        ProgressPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        ButtonsPanel.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
    }

    public void SetProgress(int done, int total, string? current)
    {
        var pct = total == 0 ? 100 : done * 100.0 / total;
        Bar.Value = pct;
        PercentText.Text = $"{pct:0}%";
        ProgressText.Text = $"{done} {Loc.T("de")} {total}" + (string.IsNullOrEmpty(current) ? "" : $" · {current}");
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!CanClose) e.Cancel = true;
        base.OnClosing(e);
    }
}
