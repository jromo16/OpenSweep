using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace OpenSweep.Core;

/// <summary>
/// Tema claro/oscuro. En modo «auto» sigue al tema de Windows y cambia en vivo.
/// La paleta propia está en Themes/Light.xaml y Themes/Dark.xaml.
/// </summary>
public static class ThemeManager
{
    public static bool IsDark { get; private set; } = true;
    public static event Action? Changed;

    private static ResourceDictionary? _palette;
    private static bool _listening;

    public static bool SystemIsDark()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return k?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch { return true; }
    }

    /// <param name="mode">auto | light | dark</param>
    public static void Apply(string mode)
    {
        IsDark = mode switch { "light" => false, "dark" => true, _ => SystemIsDark() };
        var app = Application.Current;

#pragma warning disable WPF0001
        app.ThemeMode = IsDark ? ThemeMode.Dark : ThemeMode.Light;
#pragma warning restore WPF0001

        var palette = new ResourceDictionary { Source = new Uri($"pack://application:,,,/Themes/{(IsDark ? "Dark" : "Light")}.xaml") };
        if (_palette != null) app.Resources.MergedDictionaries.Remove(_palette);
        app.Resources.MergedDictionaries.Add(palette);
        _palette = palette;

        ApplyBrandAccent(app.Resources);
        foreach (Window w in app.Windows) ApplyToWindow(w);

        if (mode == "auto" && !_listening)
        {
            SystemEvents.UserPreferenceChanged += OnSystemChanged;
            _listening = true;
        }
        else if (mode != "auto" && _listening)
        {
            SystemEvents.UserPreferenceChanged -= OnSystemChanged;
            _listening = false;
        }
        Changed?.Invoke();
    }

    private static void OnSystemChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General) return;
        Application.Current?.Dispatcher.Invoke(() =>
        {
            if (SystemIsDark() != IsDark) Apply("auto");
        });
    }

    /// <summary>
    /// El tema Fluent usa el color de acento de Windows. Lo sustituimos por el verde de OpenSweep.
    /// </summary>
    private static void ApplyBrandAccent(ResourceDictionary res)
    {
        void Set(object colorKey, object brushKey, string hex)
        {
            var c = (Color)ColorConverter.ConvertFromString(hex);
            res[colorKey] = c;
            var b = new SolidColorBrush(c);
            b.Freeze();
            res[brushKey] = b;
        }
        Set(SystemColors.AccentColorKey, SystemColors.AccentColorBrushKey, "#10B981");
        Set(SystemColors.AccentColorLight1Key, SystemColors.AccentColorLight1BrushKey, "#34D399");
        Set(SystemColors.AccentColorLight2Key, SystemColors.AccentColorLight2BrushKey, "#34D399");
        Set(SystemColors.AccentColorLight3Key, SystemColors.AccentColorLight3BrushKey, "#6EE7B7");
        Set(SystemColors.AccentColorDark1Key, SystemColors.AccentColorDark1BrushKey, "#059669");
        Set(SystemColors.AccentColorDark2Key, SystemColors.AccentColorDark2BrushKey, "#047857");
        Set(SystemColors.AccentColorDark3Key, SystemColors.AccentColorDark3BrushKey, "#065F46");
    }

    // ---------- Fondo translúcido (Mica) y barra de título ----------
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;   // 2 = Mica, 3 = Acrílico, 4 = Mica Alt

    public static bool SupportsMica => Environment.OSVersion.Version.Build >= 22621;

    public static void ApplyToWindow(Window w)
    {
        var hwnd = new WindowInteropHelper(w).Handle;
        if (hwnd == IntPtr.Zero) { w.SourceInitialized += (_, _) => ApplyToWindow(w); return; }
        int dark = IsDark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
        if (SupportsMica)
        {
            int backdrop = 2;
            DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int));
            if (HwndSource.FromHwnd(hwnd) is { CompositionTarget: not null } src) src.CompositionTarget.BackgroundColor = Colors.Transparent;
        }
    }
}
