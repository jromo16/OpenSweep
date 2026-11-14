using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OpenSweep.Core;

/// <summary>
/// Miniaturas de archivos (fotos y vídeos) usando el mismo sistema que el Explorador de Windows,
/// que además aprovecha su caché de miniaturas.
/// </summary>
public static class Thumbnails
{
    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr phbm);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE { public int cx, cy; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(string path, IntPtr pbc, [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory factory);

    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr hObject);

    private const int SIIGBF_RESIZETOFIT = 0x00, SIIGBF_BIGGERSIZEOK = 0x01, SIIGBF_THUMBNAILONLY = 0x08;

    /// <summary>Devuelve una miniatura congelada (segura entre hilos) o null.</summary>
    public static BitmapSource? Get(string path, int size)
    {
        IShellItemImageFactory? factory = null;
        IntPtr hbmp = IntPtr.Zero;
        try
        {
            SHCreateItemFromParsingName(path, IntPtr.Zero, typeof(IShellItemImageFactory).GUID, out factory);
            if (factory.GetImage(new SIZE { cx = size, cy = size }, SIIGBF_THUMBNAILONLY | SIIGBF_BIGGERSIZEOK, out hbmp) != 0 || hbmp == IntPtr.Zero)
                return null;
            var src = Imaging.CreateBitmapSourceFromHBitmap(hbmp, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            // Reducir al tamaño pedido si el sistema devolvió una más grande
            if (src.PixelWidth > size * 1.5 || src.PixelHeight > size * 1.5)
            {
                double scale = (double)size / Math.Max(src.PixelWidth, src.PixelHeight);
                src = new TransformedBitmap(src, new ScaleTransform(scale, scale));
            }
            src.Freeze();
            return src;
        }
        catch { return null; }
        finally
        {
            if (hbmp != IntPtr.Zero) DeleteObject(hbmp);
            if (factory != null) Marshal.ReleaseComObject(factory);
        }
    }

    /// <summary>Convierte una imagen a una matriz de grises de w×h (0-255).</summary>
    public static byte[]? Gray(BitmapSource src, int w, int h)
    {
        try
        {
            var scaled = new TransformedBitmap(src, new ScaleTransform((double)w / src.PixelWidth, (double)h / src.PixelHeight));
            var gray = new FormatConvertedBitmap(scaled, PixelFormats.Gray8, null, 0);
            int stride = (gray.PixelWidth + 3) & ~3;
            var buf = new byte[stride * gray.PixelHeight];
            gray.CopyPixels(buf, stride, 0);
            if (stride == gray.PixelWidth) return buf;
            var res = new byte[gray.PixelWidth * gray.PixelHeight];
            for (int y = 0; y < gray.PixelHeight; y++) Array.Copy(buf, y * stride, res, y * gray.PixelWidth, gray.PixelWidth);
            return res;
        }
        catch { return null; }
    }
}
