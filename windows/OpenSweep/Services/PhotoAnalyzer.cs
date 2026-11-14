using System.Collections.Concurrent;
using System.Numerics;
using System.Windows.Media;
using OpenSweep.Core;

namespace OpenSweep.Services;

public sealed class MediaItem
{
    public required string Path { get; init; }
    public long Size { get; init; }
    public DateTime Modified { get; init; }
    public bool IsVideo { get; init; }
    public ulong Hash { get; set; }
    public double Sharpness { get; set; }   // varianza del laplaciano (más alto = más nítida)
    public double Brightness { get; set; }  // 0-255
    public bool IsScreenshot { get; set; }
    public bool Selected { get; set; }
    public bool IsBest { get; set; }
    public int Group { get; set; }

    public string Name => System.IO.Path.GetFileName(Path);
    public string Folder => System.IO.Path.GetDirectoryName(Path) ?? "";
    public string SizeText => Util.FormatBytes(Size);
    public string DateText => Modified.ToString("dd/MM/yyyy");
    /// <summary>Copia independiente para que cada pestaña tenga su propia selección.</summary>
    public MediaItem Clone() => (MediaItem)MemberwiseClone();

    /// <summary>Miniatura cargada bajo demanda (enlazar con IsAsync=True).</summary>
    public ImageSource? Thumb => Thumbnails.Get(Path, 160);
}

public sealed class MediaGroup
{
    public required string Title { get; init; }
    public required List<MediaItem> Items { get; init; }
    public long Recoverable => Items.Where(i => i.Selected).Sum(i => i.Size);
    public string Subtitle => $"{Items.Count} {Loc.T("archivos")} · {Util.FormatBytes(Items.Sum(i => i.Size))}";
}

public sealed class MediaAnalysis
{
    public List<MediaGroup> Similar { get; } = [];
    public List<MediaGroup> SimilarVideos { get; } = [];
    public List<MediaItem> Blurry { get; } = [];
    public List<MediaItem> Dark { get; } = [];
    public List<MediaItem> Screenshots { get; } = [];
    public int Total { get; set; }
}

public static class PhotoAnalyzer
{
    public static readonly HashSet<string> ImageExt = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp", ".heic", ".heif", ".tif", ".tiff" };
    public static readonly HashSet<string> VideoExt = new(StringComparer.OrdinalIgnoreCase)
        { ".mp4", ".mov", ".mkv", ".avi", ".wmv", ".m4v", ".3gp", ".webm" };

    /// <param name="threshold">Bits distintos (de 64) para considerar dos imágenes similares. 0-4 casi idénticas, 5-10 parecidas.</param>
    public static MediaAnalysis Analyze(IEnumerable<string> roots, int threshold, IProgress<string> progress, CancellationToken ct)
    {
        var files = new List<MediaItem>();
        foreach (var root in roots)
            foreach (var f in Util.SafeFiles(root))
            {
                ct.ThrowIfCancellationRequested();
                var ext = f.Extension;
                bool img = ImageExt.Contains(ext), vid = VideoExt.Contains(ext);
                if (!img && !vid) continue;
                try
                {
                    if (f.Length < 10 * 1024) continue; // iconos y miniaturas diminutas
                    files.Add(new MediaItem { Path = f.FullName, Size = f.Length, Modified = f.LastWriteTime, IsVideo = vid });
                }
                catch { }
            }
        files = files.DistinctBy(f => f.Path.ToLowerInvariant()).ToList();

        int done = 0;
        var ok = new ConcurrentBag<MediaItem>();
        Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount / 2), CancellationToken = ct }, item =>
        {
            var thumb = Thumbnails.Get(item.Path, 128);
            if (thumb != null)
            {
                var g9 = Thumbnails.Gray(thumb, 9, 8);
                var g64 = Thumbnails.Gray(thumb, 64, 64);
                if (g9 != null && g64 != null)
                {
                    item.Hash = DHash(g9);
                    (item.Sharpness, item.Brightness) = Metrics(g64, 64);
                    item.IsScreenshot = !item.IsVideo && LooksLikeScreenshot(item.Path);
                    ok.Add(item);
                }
            }
            var n = Interlocked.Increment(ref done);
            if (n % 25 == 0) progress.Report($"{Loc.T("Analizando fotos y vídeos")}: {n:N0} de {files.Count:N0}");
        });

        var list = ok.ToList();
        var result = new MediaAnalysis { Total = list.Count };
        result.Similar.AddRange(GroupSimilar(list.Where(i => !i.IsVideo).ToList(), threshold, Loc.T("Fotos similares")));
        result.SimilarVideos.AddRange(GroupSimilar(list.Where(i => i.IsVideo).ToList(), Math.Min(threshold, 6), Loc.T("Vídeos similares")));

        var photos = list.Where(i => !i.IsVideo && !i.IsScreenshot).ToList();
        // Borrosas: nitidez muy baja respecto a la media de la colección
        if (photos.Count > 0)
        {
            var sorted = photos.Select(p => p.Sharpness).OrderBy(x => x).ToList();
            double median = sorted[sorted.Count / 2];
            double limit = Math.Min(60, median * 0.25);
            result.Blurry.AddRange(photos.Where(p => p.Sharpness < limit).OrderBy(p => p.Sharpness).Select(p => p.Clone()));
        }
        result.Dark.AddRange(photos.Where(p => p.Brightness < 28).OrderBy(p => p.Brightness).Select(p => p.Clone()));
        result.Screenshots.AddRange(list.Where(i => i.IsScreenshot).OrderBy(i => i.Modified).Select(p => p.Clone()));
        foreach (var i in result.Blurry.Concat(result.Dark).Concat(result.Screenshots)) i.IsBest = false;
        foreach (var i in result.Blurry.Concat(result.Dark)) i.Selected = true;
        foreach (var i in result.Screenshots) i.Selected = (DateTime.Now - i.Modified).TotalDays > 30;
        return result;
    }

    /// <summary>Hash de diferencias: compara cada píxel con su vecino derecho en una imagen de 9×8.</summary>
    private static ulong DHash(byte[] g)
    {
        ulong h = 0;
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
            {
                h <<= 1;
                if (g[y * 9 + x] > g[y * 9 + x + 1]) h |= 1;
            }
        return h;
    }

    private static (double sharpness, double brightness) Metrics(byte[] g, int n)
    {
        double sum = 0, sumL = 0, sumL2 = 0; int count = 0;
        for (int i = 0; i < g.Length; i++) sum += g[i];
        for (int y = 1; y < n - 1; y++)
            for (int x = 1; x < n - 1; x++)
            {
                int c = y * n + x;
                double l = 4 * g[c] - g[c - 1] - g[c + 1] - g[c - n] - g[c + n];
                sumL += l; sumL2 += l * l; count++;
            }
        double mean = sumL / count;
        return (sumL2 / count - mean * mean, sum / g.Length);
    }

    private static bool LooksLikeScreenshot(string path)
    {
        var p = path.ToLowerInvariant();
        var name = System.IO.Path.GetFileName(p);
        return p.Contains(@"\screenshots\") || p.Contains(@"\capturas de pantalla\") || p.Contains(@"\capturas\")
               || name.StartsWith("screenshot") || name.StartsWith("captura de pantalla") || name.StartsWith("captura")
               || name.StartsWith("screen shot") || name.StartsWith("snip");
    }

    private static List<MediaGroup> GroupSimilar(List<MediaItem> items, int threshold, string title)
    {
        // Agrupación por semilla: cada foto entra en el grupo cuya foto inicial se le parece.
        // (Evita encadenar A≈B≈C cuando A y C no se parecen.)
        var ordered = items.Where(i => i.Hash != 0 && i.Hash != ulong.MaxValue) // imágenes planas
                           .OrderByDescending(i => i.Size).ToList();
        var assigned = new bool[ordered.Count];
        var groups = new List<List<MediaItem>>();
        for (int a = 0; a < ordered.Count; a++)
        {
            if (assigned[a]) continue;
            var g = new List<MediaItem> { ordered[a] };
            for (int b = a + 1; b < ordered.Count; b++)
                if (!assigned[b] && BitOperations.PopCount(ordered[a].Hash ^ ordered[b].Hash) <= threshold)
                {
                    g.Add(ordered[b]);
                    assigned[b] = true;
                }
            if (g.Count > 1) groups.Add(g);
        }

        var result = new List<MediaGroup>();
        int gi = 1;
        foreach (var g in groups.OrderByDescending(g => g.Sum(i => i.Size)))
        {
            // Conservar la de mejor calidad: más nítida y más pesada (más resolución)
            var best = g.OrderByDescending(i => Math.Log(i.Size + 1) * Math.Sqrt(i.Sharpness + 1)).First();
            foreach (var i in g) { i.Group = gi; i.IsBest = i == best; i.Selected = i != best; }
            result.Add(new MediaGroup { Title = $"{title} · {Loc.T("grupo")} {gi}", Items = g.OrderByDescending(i => i.IsBest).ThenByDescending(i => i.Size).ToList() });
            gi++;
        }
        return result;
    }
}
