using System.Text.RegularExpressions;
using OpenSweep.Core;

namespace OpenSweep.Services;

public sealed class UpdateItem
{
    public required string Name { get; init; }
    public required string Id { get; init; }
    public string Current { get; init; } = "";
    public string Available { get; init; } = "";
    public string Source { get; init; } = "";
    public bool Selected { get; set; } = true;
    public string Status { get; set; } = Loc.T("Pendiente");
}

/// <summary>Usa winget (Administrador de paquetes de Windows) para encontrar e instalar actualizaciones.</summary>
public static partial class SoftwareUpdater
{
    public static bool IsAvailable()
    {
        var (code, _) = Util.Run("winget", "--version", 20_000);
        return code == 0;
    }

    public static List<UpdateItem> CheckUpdates()
    {
        var (_, output) = Util.Run("winget", "upgrade --include-unknown --accept-source-agreements --disable-interactivity", 180_000);
        return Parse(output);
    }

    public static List<UpdateItem> Parse(string output)
    {
        var list = new List<UpdateItem>();
        // winget usa \r para animaciones: quedarse con el texto final de cada línea
        var lines = output.Replace("\r\n", "\n").Split('\n')
            .Select(l => { var i = l.LastIndexOf('\r'); return i >= 0 ? l[(i + 1)..] : l; })
            .ToList();
        int sep = lines.FindIndex(l => l.Trim().Length > 10 && l.Trim().All(c => c == '-' || c == '─'));
        if (sep < 1) return list;
        var header = lines[sep - 1];
        // posiciones de inicio de cada columna (Nombre, Id, Versión, Disponible, Origen)
        var starts = HeaderRegex().Matches(header).Select(m => m.Index).ToList();
        if (starts.Count < 4) return list;
        for (int i = sep + 1; i < lines.Count; i++)
        {
            var line = lines[i];
            if (line.Trim().Length == 0) break; // fin de la tabla
            if (line.Length < starts[3]) continue;
            string Col(int c)
            {
                var s = starts[c];
                var e = c + 1 < starts.Count ? starts[c + 1] : line.Length;
                if (s >= line.Length) return "";
                return line[s..Math.Min(e, line.Length)].Trim();
            }
            var id = Col(1);
            if (id.Length == 0 || id.Contains(' ')) continue;
            list.Add(new UpdateItem
            {
                Name = Col(0), Id = id, Current = Col(2), Available = Col(3), Source = starts.Count > 4 ? Col(4) : "",
            });
        }
        return list;
    }

    public static async Task<bool> UpgradeAsync(UpdateItem item)
    {
        return await Task.Run(() =>
        {
            var (code, _) = Util.Run("winget",
                $"upgrade --id \"{item.Id}\" -e --silent --accept-package-agreements --accept-source-agreements --disable-interactivity",
                30 * 60_000);
            return code == 0;
        });
    }

    [GeneratedRegex(@"\S+")]
    private static partial Regex HeaderRegex();
}
