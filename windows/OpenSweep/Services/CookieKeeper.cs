using Microsoft.Data.Sqlite;
using OpenSweep.Core;

namespace OpenSweep.Services;

/// <summary>
/// Borra cookies de forma selectiva conservando las de los sitios de la lista blanca
/// (para no cerrar la sesión en Gmail, el banco, etc.).
/// </summary>
public static class CookieKeeper
{
    public static readonly string[] CommonSites =
    [
        "google.com", "youtube.com", "gmail.com", "microsoft.com", "live.com", "outlook.com", "office.com",
        "facebook.com", "instagram.com", "whatsapp.com", "x.com", "twitter.com", "github.com", "linkedin.com",
        "amazon.com", "netflix.com", "spotify.com", "apple.com", "icloud.com", "paypal.com",
    ];

    private static string Condition(IReadOnlyList<string> domains, string column, SqliteCommand cmd)
    {
        if (domains.Count == 0) return "1=1";
        var parts = new List<string>();
        for (int i = 0; i < domains.Count; i++)
        {
            var d = domains[i].Trim().TrimStart('.').ToLowerInvariant();
            cmd.Parameters.AddWithValue($"$d{i}", d);
            cmd.Parameters.AddWithValue($"$dd{i}", "." + d);
            cmd.Parameters.AddWithValue($"$l{i}", "%." + d);
            parts.Add($"({column} = $d{i} OR {column} = $dd{i} OR {column} LIKE $l{i})");
        }
        // Se borran las cookies que NO coinciden con ningún dominio protegido
        return "NOT (" + string.Join(" OR ", parts) + ")";
    }

    private static (string table, string column) Schema(string dbPath) =>
        Path.GetFileName(dbPath).Equals("cookies.sqlite", StringComparison.OrdinalIgnoreCase) ? ("moz_cookies", "host") : ("cookies", "host_key");

    /// <summary>Cuenta (o borra) las cookies no protegidas de una base de datos.</summary>
    public static long Process(string dbPath, IReadOnlyList<string> keep, bool delete)
    {
        if (!File.Exists(dbPath)) return 0;
        try
        {
            using var con = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = delete ? SqliteOpenMode.ReadWrite : SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
            con.Open();
            var (table, column) = Schema(dbPath);
            using var cmd = con.CreateCommand();
            var where = Condition(keep, column, cmd);
            cmd.CommandText = delete ? $"DELETE FROM {table} WHERE {where}" : $"SELECT COUNT(*) FROM {table} WHERE {where}";
            var r = delete ? cmd.ExecuteNonQuery() : Convert.ToInt64(cmd.ExecuteScalar());
            if (delete)
            {
                using var vac = con.CreateCommand();
                vac.CommandText = "VACUUM";
                vac.ExecuteNonQuery();
            }
            return r;
        }
        catch { return 0; }
    }

    /// <summary>Dominios con cookies en un archivo (para sugerirlos en la lista blanca).</summary>
    public static Dictionary<string, int> Domains(string dbPath)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(dbPath)) return result;
        // Copia temporal: el navegador puede tener el archivo bloqueado
        var tmp = Path.Combine(Path.GetTempPath(), "os_cookies_" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            File.Copy(dbPath, tmp, true);
            using var con = new SqliteConnection($"Data Source={tmp};Mode=ReadOnly;Pooling=False");
            con.Open();
            var (table, column) = Schema(dbPath);
            using var cmd = con.CreateCommand();
            cmd.CommandText = $"SELECT {column}, COUNT(*) FROM {table} GROUP BY {column}";
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                var host = rd.GetString(0).TrimStart('.');
                // reducir a dominio principal: accounts.google.com -> google.com
                var parts = host.Split('.');
                var root = parts.Length >= 3 && parts[^2].Length <= 3 && parts[^1].Length == 2 ? string.Join('.', parts[^3..]) :
                           parts.Length >= 2 ? string.Join('.', parts[^2..]) : host;
                result[root] = result.GetValueOrDefault(root) + rd.GetInt32(1);
            }
        }
        catch { }
        finally { try { File.Delete(tmp); } catch { } }
        return result;
    }

    /// <summary>Todas las bases de datos de cookies de los navegadores instalados.</summary>
    public static IEnumerable<string> AllCookieDatabases()
    {
        foreach (var ud in new[]
                 {
                     Path.Combine(Util.LocalAppData, @"Google\Chrome\User Data"), Path.Combine(Util.LocalAppData, @"Microsoft\Edge\User Data"),
                     Path.Combine(Util.LocalAppData, @"BraveSoftware\Brave-Browser\User Data"), Path.Combine(Util.LocalAppData, @"Vivaldi\User Data"),
                     Path.Combine(Util.RoamingAppData, @"Opera Software\Opera Stable"), Path.Combine(Util.RoamingAppData, @"Opera Software\Opera GX Stable"),
                 })
            foreach (var p in JunkCleaner.ChromiumProfiles(ud))
            {
                var a = Path.Combine(p, "Network", "Cookies");
                if (File.Exists(a)) yield return a;
                else if (File.Exists(Path.Combine(p, "Cookies"))) yield return Path.Combine(p, "Cookies");
            }
        foreach (var p in Util.SafeDirs(Path.Combine(Util.RoamingAppData, @"Mozilla\Firefox\Profiles")))
        {
            var f = Path.Combine(p.FullName, "cookies.sqlite");
            if (File.Exists(f)) yield return f;
        }
    }
}
