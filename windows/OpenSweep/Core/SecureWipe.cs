using System.Security.Cryptography;

namespace OpenSweep.Core;

/// <summary>Sobrescritura segura de archivos y espacio libre.</summary>
public static class SecureWipe
{
    private const int BufferSize = 1024 * 1024;

    /// <summary>Sobrescribe el contenido de un archivo con datos aleatorios (passes veces) y ceros al final.</summary>
    public static void OverwriteFile(string path, int passes, CancellationToken ct = default)
    {
        var fi = new FileInfo(path);
        if (!fi.Exists) return;
        long length = fi.Length;
        var buffer = new byte[BufferSize];
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None, BufferSize, FileOptions.WriteThrough);
        for (int pass = 0; pass < passes; pass++)
        {
            fs.Position = 0;
            long remaining = length;
            bool zeros = pass == passes - 1 && passes > 1;
            while (remaining > 0)
            {
                ct.ThrowIfCancellationRequested();
                int n = (int)Math.Min(buffer.Length, remaining);
                if (zeros) Array.Clear(buffer, 0, n); else RandomNumberGenerator.Fill(buffer.AsSpan(0, n));
                fs.Write(buffer, 0, n);
                remaining -= n;
            }
            fs.Flush(true);
        }
    }

    /// <summary>Destruye un archivo: sobrescribe, renombra con nombre aleatorio y elimina.</summary>
    public static bool ShredFile(string path, int passes, CancellationToken ct = default)
    {
        try
        {
            File.SetAttributes(path, FileAttributes.Normal);
            OverwriteFile(path, passes, ct);
            var dir = Path.GetDirectoryName(path)!;
            var randomName = Path.Combine(dir, Path.GetRandomFileName());
            File.Move(path, randomName);
            using (var fs = new FileStream(randomName, FileMode.Open, FileAccess.Write)) fs.SetLength(0);
            File.Delete(randomName);
            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch { return false; }
    }

    /// <summary>
    /// Llena el espacio libre de una unidad con datos (aleatorios o ceros) para que los
    /// archivos borrados anteriormente no puedan recuperarse. Luego borra el archivo de relleno.
    /// </summary>
    public static void WipeFreeSpace(string driveRoot, int passes, IProgress<(int pass, double percent)> progress, CancellationToken ct)
    {
        var tempDir = Path.Combine(driveRoot, "OpenSweep_Wipe_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);
        try
        {
            var buffer = new byte[BufferSize * 4];
            for (int pass = 1; pass <= passes; pass++)
            {
                var drive = new DriveInfo(driveRoot);
                long initialFree = drive.AvailableFreeSpace;
                long written = 0;
                int fileIndex = 0;
                bool full = false;
                bool zeros = pass == passes && passes > 1;
                while (!full)
                {
                    var file = Path.Combine(tempDir, $"wipe{fileIndex++}.tmp");
                    try
                    {
                        using var fs = new FileStream(file, FileMode.Create, FileAccess.Write, FileShare.None, buffer.Length, FileOptions.WriteThrough);
                        long fileBytes = 0;
                        // Archivos de máximo 2 GB para no superar los límites de FAT32.
                        while (fileBytes < 2L * 1024 * 1024 * 1024 - buffer.Length)
                        {
                            ct.ThrowIfCancellationRequested();
                            long free = new DriveInfo(driveRoot).AvailableFreeSpace;
                            if (free < 64L * 1024 * 1024) { full = true; break; } // deja 64 MB al sistema
                            int n = (int)Math.Min(buffer.Length, free - 32L * 1024 * 1024);
                            if (n <= 0) { full = true; break; }
                            if (zeros) Array.Clear(buffer, 0, n); else RandomNumberGenerator.Fill(buffer.AsSpan(0, n));
                            fs.Write(buffer, 0, n);
                            fileBytes += n;
                            written += n;
                            progress.Report((pass, initialFree <= 0 ? 100 : Math.Min(100, written * 100.0 / initialFree)));
                        }
                    }
                    catch (IOException) { full = true; }
                }
                foreach (var f in Directory.GetFiles(tempDir)) { try { File.Delete(f); } catch { } }
            }
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }
}
