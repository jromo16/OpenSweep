using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace OpenSweep.Core;

/// <summary>
/// Ajusta las ventanas a pantallas pequeñas (portátiles, escalado de 125-150 %).
/// </summary>
public static class WindowFit
{
    /// <summary>
    /// Tamaño preferido, pero nunca mayor que el área de trabajo de la pantalla (sin la barra de tareas).
    /// Así la barra de título y sus botones siempre quedan a la vista.
    /// </summary>
    public static void FitToWorkArea(Window w, double preferredWidth, double preferredHeight, double minWidth, double minHeight)
    {
        var area = SystemParameters.WorkArea;   // en unidades WPF (ya tiene en cuenta el escalado)
        const double margin = 24;
        w.Width = Math.Min(preferredWidth, area.Width - margin);
        w.Height = Math.Min(preferredHeight, area.Height - margin);
        w.MinWidth = Math.Min(minWidth, area.Width - margin);
        w.MinHeight = Math.Min(minHeight, area.Height - margin);

        // Al abrir, comprobar de nuevo en la pantalla real donde aparece (puede ser otro monitor)
        w.SourceInitialized += (_, _) => KeepInsideScreen(w);
    }

    /// <summary>Si la ventana queda fuera de su monitor, la reduce y la vuelve a centrar.</summary>
    public static void KeepInsideScreen(Window w)
    {
        var hwnd = new WindowInteropHelper(w).Handle;
        if (hwnd == IntPtr.Zero || w.WindowState == WindowState.Maximized) return;
        var work = MonitorWorkArea(hwnd, w);
        if (work == Rect.Empty) return;
        if (w.Width > work.Width) w.Width = work.Width;
        if (w.Height > work.Height) w.Height = work.Height;
        if (w.Left < work.Left || w.Top < work.Top || w.Left + w.Width > work.Right || w.Top + w.Height > work.Bottom)
        {
            w.Left = work.Left + (work.Width - w.Width) / 2;
            w.Top = work.Top + (work.Height - w.Height) / 2;
        }
    }

    /// <summary>
    /// Con una barra de título propia (WindowChrome), al maximizar la ventana sobresale unos píxeles
    /// por cada borde y los botones de cerrar/minimizar quedan cortados. Este margen lo compensa.
    /// </summary>
    public static Thickness MaximizedMargin(Window w)
    {
        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(w);
        int frameX = GetSystemMetrics(SM_CXFRAME) + GetSystemMetrics(SM_CXPADDEDBORDER);
        int frameY = GetSystemMetrics(SM_CYFRAME) + GetSystemMetrics(SM_CXPADDEDBORDER);
        return new Thickness(frameX / dpi.DpiScaleX, frameY / dpi.DpiScaleY, frameX / dpi.DpiScaleX, frameY / dpi.DpiScaleY);
    }

    // ---------- Win32 ----------
    private const int SM_CXFRAME = 32, SM_CYFRAME = 33, SM_CXPADDEDBORDER = 92;
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO info);

    private static Rect MonitorWorkArea(IntPtr hwnd, Window w)
    {
        var mon = MonitorFromWindow(hwnd, 2 /* MONITOR_DEFAULTTONEAREST */);
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(mon, ref info)) return Rect.Empty;
        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(w);
        return new Rect(info.rcWork.Left / dpi.DpiScaleX, info.rcWork.Top / dpi.DpiScaleY,
            (info.rcWork.Right - info.rcWork.Left) / dpi.DpiScaleX, (info.rcWork.Bottom - info.rcWork.Top) / dpi.DpiScaleY);
    }
}
