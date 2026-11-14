using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace OpenSweep;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnUnhandled;
        ApplyBrandAccent();
        base.OnStartup(e);
    }

    /// <summary>
    /// El tema Fluent usa el color de acento de Windows (SystemColors.AccentColor*).
    /// Lo sustituimos por la paleta verde de OpenSweep a nivel de aplicación.
    /// </summary>
    private void ApplyBrandAccent()
    {
        void Set(object colorKey, object brushKey, string hex)
        {
            var c = (Color)ColorConverter.ConvertFromString(hex);
            Resources[colorKey] = c;
            var b = new SolidColorBrush(c);
            b.Freeze();
            Resources[brushKey] = b;
        }
        Set(SystemColors.AccentColorKey, SystemColors.AccentColorBrushKey, "#10B981");
        Set(SystemColors.AccentColorLight1Key, SystemColors.AccentColorLight1BrushKey, "#34D399");
        Set(SystemColors.AccentColorLight2Key, SystemColors.AccentColorLight2BrushKey, "#34D399");
        Set(SystemColors.AccentColorLight3Key, SystemColors.AccentColorLight3BrushKey, "#6EE7B7");
        Set(SystemColors.AccentColorDark1Key, SystemColors.AccentColorDark1BrushKey, "#059669");
        Set(SystemColors.AccentColorDark2Key, SystemColors.AccentColorDark2BrushKey, "#047857");
        Set(SystemColors.AccentColorDark3Key, SystemColors.AccentColorDark3BrushKey, "#065F46");
    }

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show("Se produjo un error inesperado:\n\n" + e.Exception.Message, "OpenSweep", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
