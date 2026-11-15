namespace OpenSweep.Tests;

/// <summary>
/// Carpeta temporal para una prueba; se borra al terminar.
/// Se crea junto a las pruebas (no en %TEMP%) porque OpenSweep excluye AppData de algunos análisis.
/// </summary>
public sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(AppContext.BaseDirectory, "tmp-" + Guid.NewGuid().ToString("N")[..8]);

    public TempDir() => Directory.CreateDirectory(Path);

    public string File(string relative, string content = "x", DateTime? modified = null)
    {
        var full = System.IO.Path.Combine(Path, relative);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        System.IO.File.WriteAllText(full, content);
        if (modified != null) System.IO.File.SetLastWriteTime(full, modified.Value);
        return full;
    }

    public void Dispose()
    {
        try { Directory.Delete(Path, true); } catch { }
    }
}
