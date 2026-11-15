using Microsoft.Data.Sqlite;
using Microsoft.Win32;
using OpenSweep.Core;
using OpenSweep.Services;

namespace OpenSweep.Tests;

public class DuplicatesTests
{
    [Fact]
    public void Encuentra_duplicados_por_contenido_y_conserva_el_mas_antiguo()
    {
        using var t = new TempDir();
        var contenido = new string('a', 4096);
        var original = t.File("fotos\\original.jpg", contenido, DateTime.Now.AddDays(-10));
        File.SetCreationTime(original, DateTime.Now.AddDays(-10));
        var copia = t.File("descargas\\copia.jpg", contenido);
        t.File("otra.jpg", new string('b', 4096));            // mismo tamaño, distinto contenido

        var groups = DiskTools.FindDuplicates([t.Path], 1, false, new Progress<string>(), CancellationToken.None);

        var g = Assert.Single(groups);
        Assert.Equal(2, g.Count);
        Assert.False(g.First(f => f.Path == original).Selected);   // el más antiguo queda sin marcar
        Assert.True(g.First(f => f.Path == copia).Selected);
    }

    [Fact]
    public void No_analiza_carpetas_de_programas()
    {
        // En AppData hay DLL y recursos repetidos a propósito: borrarlos rompería programas
        var dir = Path.Combine(Path.GetTempPath(), "os-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "a.dll"), new string('z', 4096));
            File.WriteAllText(Path.Combine(dir, "b.dll"), new string('z', 4096));
            Assert.Empty(DiskTools.FindDuplicates([dir], 1, false, new Progress<string>(), CancellationToken.None));
        }
        finally { Directory.Delete(dir, true); }
    }
}

public class CookieTests
{
    private static string MakeDb(string dir, params string[] hosts)
    {
        var path = Path.Combine(dir, "Cookies");
        using var con = new SqliteConnection($"Data Source={path};Pooling=False");
        con.Open();
        using (var cmd = con.CreateCommand())
        {
            cmd.CommandText = "CREATE TABLE cookies (host_key TEXT, name TEXT)";
            cmd.ExecuteNonQuery();
        }
        foreach (var h in hosts)
        {
            using var cmd = con.CreateCommand();
            cmd.CommandText = "INSERT INTO cookies VALUES ($h, 'c')";
            cmd.Parameters.AddWithValue("$h", h);
            cmd.ExecuteNonQuery();
        }
        return path;
    }

    [Fact]
    public void Conserva_las_cookies_de_los_sitios_protegidos()
    {
        using var t = new TempDir();
        var db = MakeDb(t.Path, ".google.com", "accounts.google.com", "google.com", ".ads.example.com", "tracker.net", "notgoogle.com");
        string[] keep = ["google.com"];

        Assert.Equal(3, CookieKeeper.Process(db, keep, delete: false));   // cuenta sin borrar
        Assert.Equal(3, CookieKeeper.Process(db, keep, delete: true));
        Assert.Equal(0, CookieKeeper.Process(db, keep, delete: false));   // solo quedan las de Google
        Assert.Equal(0, CookieKeeper.Process(db, [], delete: false) - 3); // y son 3
    }

    [Fact]
    public void Lista_los_dominios_principales()
    {
        using var t = new TempDir();
        var db = MakeDb(t.Path, ".google.com", "accounts.google.com", "mail.example.co.uk");
        var d = CookieKeeper.Domains(db);
        Assert.Equal(2, d["google.com"]);
        Assert.Equal(1, d["example.co.uk"]);
    }
}

public class UninstallerTests
{
    private static InstalledProgram Program(string name, string location) => new()
    {
        Name = name, InstallLocation = location, Hive = RegistryHive.LocalMachine, View = RegistryView.Registry64, KeyName = name,
    };

    [Fact]
    public void Modo_cazador_encuentra_el_programa_por_su_carpeta()
    {
        using var t = new TempDir();
        var exe = t.File("MiApp\\bin\\miapp.exe");
        var programs = new[] { Program("Otra", Path.Combine(t.Path, "Otra")), Program("Mi App", Path.Combine(t.Path, "MiApp")) };

        Assert.Equal("Mi App", UninstallerService.FindOwner(exe, programs)?.Name);
    }

    [Fact]
    public void Modo_cazador_elige_la_carpeta_mas_especifica()
    {
        using var t = new TempDir();
        var exe = t.File("Suite\\Editor\\editor.exe");
        var programs = new[] { Program("Suite", Path.Combine(t.Path, "Suite")), Program("Editor", Path.Combine(t.Path, "Suite", "Editor")) };

        Assert.Equal("Editor", UninstallerService.FindOwner(exe, programs)?.Name);
    }
}

public class InstallMonitorTests
{
    [Fact]
    public void Solo_registra_lo_nuevo_y_de_nivel_superior()
    {
        var before = new Snapshot();
        before.Dirs.Add(@"C:\Program Files\Existente");
        before.Keys.Add(@"HKLM|64|SOFTWARE\Existente");

        var after = new Snapshot();
        after.Dirs.UnionWith([@"C:\Program Files\Existente", @"C:\Program Files\Nuevo", @"C:\Program Files\Nuevo\bin", @"C:\Program Files\Nuevo\bin\x"]);
        after.Files.Add(@"C:\Users\Public\Desktop\Nuevo.lnk");
        after.Keys.UnionWith([@"HKLM|64|SOFTWARE\Existente", @"HKLM|64|SOFTWARE\Nuevo", @"HKLM|64|SOFTWARE\Nuevo\Config"]);
        after.Values.Add(@"HKCU|64|Software\Microsoft\Windows\CurrentVersion\Run|Nuevo");

        var trace = InstallMonitor.Diff(before, after, "nuevo-setup.exe");

        Assert.Contains(trace.Items, i => i.Kind == "dir" && i.Path == @"C:\Program Files\Nuevo");
        Assert.DoesNotContain(trace.Items, i => i.Path.StartsWith(@"C:\Program Files\Nuevo\"));   // las subcarpetas van incluidas
        Assert.Contains(trace.Items, i => i.Kind == "file" && i.Path.EndsWith("Nuevo.lnk"));
        Assert.Single(trace.Items, i => i.Kind == "key");                                         // solo la clave superior
        Assert.Contains(trace.Items, i => i.Kind == "value" && i.ValueName == "Nuevo");
        Assert.DoesNotContain(trace.Items, i => i.Path.Contains("Existente"));
    }
}

public class UpdaterTests
{
    [Fact]
    public void Lee_la_tabla_de_winget_en_español()
    {
        const string output = """
            Nombre                 Id                     Versión  Disponible Origen
            ------------------------------------------------------------------------
            Git                    Git.Git                2.55.0   2.56.0     winget
            Node.js                OpenJS.NodeJS          26.9.0   26.12.1    winget
            2 actualizaciones disponibles.
            """;
        var items = SoftwareUpdater.Parse(output);
        Assert.Equal(2, items.Count);
        Assert.Equal("Git.Git", items[0].Id);
        Assert.Equal("2.56.0", items[0].Available);
        Assert.Equal("26.9.0", items[1].Current);
    }

    [Fact]
    public void Salida_sin_tabla_no_falla() => Assert.Empty(SoftwareUpdater.Parse("No se encontraron actualizaciones."));
}

public class TranslationTests
{
    [Fact]
    public void El_ingles_carga_y_traduce()
    {
        Loc.Init("en");
        try
        {
            Assert.Equal("Cancel", Loc.T("Cancelar"));
            Assert.Equal("texto sin traducción", Loc.T("texto sin traducción"));   // sin traducción: se deja en español
        }
        finally { Loc.Init("es"); }
    }

    [Fact]
    public void El_archivo_de_traducciones_no_tiene_valores_vacios()
    {
        var json = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "i18n", "en.json");
        var map = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(json))!;
        Assert.True(map.Count > 500);
        Assert.All(map, kv => Assert.False(string.IsNullOrWhiteSpace(kv.Value), $"Falta traducir: {kv.Key}"));
    }
}
