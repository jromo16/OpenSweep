using System.Security.Cryptography;
using OpenSweep.Core;

namespace OpenSweep.Services;

public sealed class FileEntry
{
    public required string Path { get; init; }
    public long Size { get; init; }
    public DateTime Modified { get; init; }
    public string Category { get; init; } = "";
    public bool Selected { get; set; }
    public string Name => System.IO.Path.GetFileName(Path);
    public string Folder => System.IO.Path.GetDirectoryName(Path) ?? "";
    public string SizeText => Util.FormatBytes(Size);
    public string DateText => Modified.ToString("dd/MM/yyyy HH:mm");
    public int Group { get; set; }
}

public sealed class CategoryStat
{
    public required string Name { get; init; }
    public long Bytes { get; set; }
    public long Count { get; set; }
    public double Percent { get; set; }
    public string SizeText => Util.FormatBytes(Bytes);
}

public sealed class FolderStat
{
    public required string Path { get; init; }
    public long Bytes { get; set; }
    public double Percent { get; set; }
    public string SizeText => Util.FormatBytes(Bytes);
}

public sealed class DiskAnalysis
{
    public List<CategoryStat> Categories { get; } = [];
    public List<FolderStat> Folders { get; } = [];
    public List<FileEntry> LargestFiles { get; } = [];
    public long TotalBytes { get; set; }
    public long TotalFiles { get; set; }
}

public static class DiskTools
{
    private static readonly Dictionary<string, string> Categories = BuildCategories();

    private static Dictionary<string, string> BuildCategories()
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        void Add(string cat, string exts) { foreach (var e in exts.Split(' ')) d["." + e] = cat; }
        Add("Imágenes", "jpg jpeg png gif bmp tif tiff webp heic heif raw cr2 nef arw svg ico psd");
        Add("Música", "mp3 wav flac aac ogg wma m4a opus aiff mid");
        Add("Vídeos", "mp4 mkv avi mov wmv flv webm m4v mpg mpeg 3gp ts");
        Add("Documentos", "pdf doc docx xls xlsx ppt pptx odt ods odp txt rtf csv md epub");
        Add("Comprimidos", "zip rar 7z tar gz bz2 xz iso img cab");
        Add("Programas", "exe msi dll sys bat cmd ps1 apk appx msix");
        Add("Correo", "pst ost eml msg mbox");
        Add("Temporales", "tmp temp bak old log dmp chk");
        return d;
    }

    public static string CategoryOf(string path) =>
        Categories.TryGetValue(Path.GetExtension(path), out var c) ? c : "Otros";

    public static DiskAnalysis Analyze(string root, int topFiles, IProgress<string> progress, CancellationToken ct)
    {
        var result = new DiskAnalysis();
        var cats = new Dictionary<string, CategoryStat>();
        var folders = new Dictionary<string, FolderStat>(StringComparer.OrdinalIgnoreCase);
        var top = new PriorityQueue<FileEntry, long>();
        root = root.TrimEnd('\\') + "\\";
        long n = 0;
        foreach (var f in Util.SafeFiles(root))
        {
            ct.ThrowIfCancellationRequested();
            long len;
            try { len = f.Length; } catch { continue; }
            var cat = CategoryOf(f.Name);
            if (!cats.TryGetValue(cat, out var cs)) cats[cat] = cs = new CategoryStat { Name = cat };
            cs.Bytes += len; cs.Count++;

            var rel = f.FullName.Length > root.Length ? f.FullName[root.Length..] : f.Name;
            var slash = rel.IndexOf('\\');
            var top1 = slash > 0 ? root + rel[..slash] : "(archivos en la raíz)";
            if (!folders.TryGetValue(top1, out var fs)) folders[top1] = fs = new FolderStat { Path = top1 };
            fs.Bytes += len;

            result.TotalBytes += len; result.TotalFiles++;
            if (top.Count < topFiles) top.Enqueue(new FileEntry { Path = f.FullName, Size = len, Modified = f.LastWriteTime, Category = cat }, len);
            else if (len > top.Peek().Size) { top.Dequeue(); top.Enqueue(new FileEntry { Path = f.FullName, Size = len, Modified = f.LastWriteTime, Category = cat }, len); }

            if (++n % 2000 == 0) progress.Report($"{n:N0} archivos analizados · {Util.FormatBytes(result.TotalBytes)} · {f.DirectoryName}");
        }
        foreach (var c in cats.Values.OrderByDescending(c => c.Bytes))
        {
            c.Percent = result.TotalBytes == 0 ? 0 : c.Bytes * 100.0 / result.TotalBytes;
            result.Categories.Add(c);
        }
        foreach (var f in folders.Values.OrderByDescending(f => f.Bytes))
        {
            f.Percent = result.TotalBytes == 0 ? 0 : f.Bytes * 100.0 / result.TotalBytes;
            result.Folders.Add(f);
        }
        var files = new List<FileEntry>();
        while (top.Count > 0) files.Add(top.Dequeue());
        files.Reverse();
        result.LargestFiles.AddRange(files);
        return result;
    }

    /// <summary>Busca archivos duplicados: agrupa por tamaño, luego por hash parcial y finalmente por SHA-256 completo.</summary>
    public static List<List<FileEntry>> FindDuplicates(IEnumerable<string> roots, long minSize, bool matchName, IProgress<string> progress, CancellationToken ct)
    {
        var bySize = new Dictionary<long, List<FileInfo>>();
        long scanned = 0;
        var winDir = Util.WinDir;
        foreach (var root in roots)
        {
            foreach (var f in Util.SafeFiles(root))
            {
                ct.ThrowIfCancellationRequested();
                long len;
                try { len = f.Length; } catch { continue; }
                if (len < minSize || len == 0) continue;
                if (f.FullName.StartsWith(winDir, StringComparison.OrdinalIgnoreCase)) continue;
                if (!bySize.TryGetValue(len, out var l)) bySize[len] = l = [];
                l.Add(f);
                if (++scanned % 2000 == 0) progress.Report($"{scanned:N0} archivos encontrados...");
            }
        }

        var groups = new List<List<FileEntry>>();
        var candidates = bySize.Where(kv => kv.Value.Count > 1).ToList();
        int i = 0;
        foreach (var (size, files) in candidates)
        {
            ct.ThrowIfCancellationRequested();
            i++;
            if (i % 50 == 0) progress.Report($"Comparando contenido: grupo {i:N0} de {candidates.Count:N0}");
            IEnumerable<IGrouping<string, FileInfo>> sets = matchName
                ? files.GroupBy(f => f.Name.ToLowerInvariant())
                : [.. files.GroupBy(_ => "")];
            foreach (var set in sets)
            {
                if (set.Count() < 2) continue;
                var partial = set.GroupBy(f => Hash(f.FullName, 64 * 1024)).Where(g => g.Key != null && g.Count() > 1);
                foreach (var pg in partial)
                {
                    var full = size <= 64 * 1024 ? [pg] : pg.GroupBy(f => Hash(f.FullName, long.MaxValue)).Where(g => g.Key != null && g.Count() > 1);
                    foreach (var fg in full)
                    {
                        var group = fg.OrderBy(f => f.CreationTime)
                            .Select(f => new FileEntry { Path = f.FullName, Size = f.Length, Modified = f.LastWriteTime, Category = CategoryOf(f.Name) })
                            .ToList();
                        groups.Add(group);
                    }
                }
            }
        }
        int gi = 1;
        foreach (var g in groups.OrderByDescending(g => g[0].Size * (g.Count - 1)))
        {
            foreach (var f in g) f.Group = gi;
            for (int k = 1; k < g.Count; k++) g[k].Selected = true; // mantener el más antiguo
            gi++;
        }
        return groups.OrderBy(g => g[0].Group).ToList();
    }

    private static string? Hash(string path, long maxBytes)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1024 * 1024);
            using var sha = SHA256.Create();
            if (maxBytes >= fs.Length) return Convert.ToHexString(sha.ComputeHash(fs));
            var buf = new byte[maxBytes];
            int read = fs.Read(buf, 0, buf.Length);
            return Convert.ToHexString(sha.ComputeHash(buf, 0, read));
        }
        catch { return null; }
    }
}
