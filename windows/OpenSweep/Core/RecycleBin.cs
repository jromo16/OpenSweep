using System.Security.Principal;
using System.Text;

namespace OpenSweep.Core;

/// <summary>
/// Lee y restaura elementos de la Papelera de reciclaje de Windows.
/// Cada elemento borrado son dos entradas en «X:\$Recycle.Bin\SID\»: «$Ixxxxxx» (ruta original y fecha)
/// y «$Rxxxxxx» (el archivo o carpeta). Restaurar es devolver «$R» a su ruta original y quitar «$I»,
/// lo mismo que hace «Restaurar» en el Explorador.
/// </summary>
public static class RecycleBin
{
    public sealed record Entry(string OriginalPath, DateTime Deleted, string InfoFile, string DataPath);

    private static string? Sid => WindowsIdentity.GetCurrent().User?.Value;

    /// <summary>Lee un archivo $I (formatos de Windows Vista/7/8 y de Windows 10/11).</summary>
    internal static (string path, DateTime deleted)? ReadInfo(byte[] data)
    {
        if (data.Length < 24) return null;
        long version = BitConverter.ToInt64(data, 0);
        var deleted = DateTime.FromFileTime(BitConverter.ToInt64(data, 16));
        string path;
        if (version == 2)
        {
            if (data.Length < 28) return null;
            int chars = BitConverter.ToInt32(data, 24);
            if (chars <= 0 || 28 + chars * 2 > data.Length) return null;
            path = Encoding.Unicode.GetString(data, 28, chars * 2);
        }
        else if (version == 1)
        {
            path = Encoding.Unicode.GetString(data, 24, Math.Min(520, data.Length - 24));
        }
        else return null;
        return (path.TrimEnd('\0'), deleted);
    }

    /// <summary>Elementos de la papelera del usuario actual en todas las unidades.</summary>
    public static List<Entry> List()
    {
        var list = new List<Entry>();
        if (Sid == null) return list;
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
        {
            var dir = Path.Combine(drive.RootDirectory.FullName, "$Recycle.Bin", Sid);
            foreach (var info in Util.SafeFiles(dir, "$I*", false))
            {
                try
                {
                    var parsed = ReadInfo(File.ReadAllBytes(info.FullName));
                    if (parsed == null) continue;
                    var data = Path.Combine(dir, "$R" + info.Name[2..]);
                    if (File.Exists(data) || Directory.Exists(data))
                        list.Add(new Entry(parsed.Value.path, parsed.Value.deleted, info.FullName, data));
                }
                catch { }
            }
        }
        return list;
    }

    /// <summary>
    /// Devuelve a su sitio los elementos cuya ruta original está en <paramref name="paths"/>
    /// (si hay varios borrados con la misma ruta, el más reciente). Devuelve las rutas restauradas.
    /// </summary>
    public static List<string> Restore(IEnumerable<string> paths, DateTime? deletedAfter = null)
    {
        var wanted = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
        var restored = new List<string>();
        // Si se indica desde cuándo, se elige el borrado más cercano a esa fecha (el de esa desinstalación, no uno posterior)
        var candidates = List().Where(e => wanted.Contains(e.OriginalPath) && (deletedAfter == null || e.Deleted >= deletedAfter.Value));
        candidates = deletedAfter == null ? candidates.OrderByDescending(e => e.Deleted) : candidates.OrderBy(e => e.Deleted);
        foreach (var e in candidates.GroupBy(e => e.OriginalPath, StringComparer.OrdinalIgnoreCase).Select(g => g.First()))
        {
            try
            {
                if (File.Exists(e.OriginalPath) || Directory.Exists(e.OriginalPath)) continue;   // no pisar nada
                Directory.CreateDirectory(Path.GetDirectoryName(e.OriginalPath)!);
                if (Directory.Exists(e.DataPath)) Directory.Move(e.DataPath, e.OriginalPath);
                else File.Move(e.DataPath, e.OriginalPath);
                File.Delete(e.InfoFile);
                restored.Add(e.OriginalPath);
            }
            catch { }
        }
        return restored;
    }
}
