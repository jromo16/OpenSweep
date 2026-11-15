using System.Windows;
using Microsoft.Win32;
using OpenSweep.Core;
using OpenSweep.Services;

namespace OpenSweep.Tests;

public class Winapp2Tests
{
    [Fact]
    public void Lee_las_entradas_y_descarta_navegadores_y_categorias_desconocidas()
    {
        using var t = new TempDir();
        var ini = t.File("winapp2.ini", """
            ; comentario
            [Programa de prueba *]
            LangSecRef=3021
            DetectFile=%AppData%\Prueba
            FileKey1=%AppData%\Prueba\Cache|*.*|RECURSE
            ExcludeKey1=FILE|%AppData%\Prueba\Cache\|config.json
            Warning=Cuidado

            [Juego]
            Section=Games
            FileKey1=C:\Juego|*.log

            [Navegador]
            Section=Chromium Browsers
            FileKey1=C:\Nav|*.tmp

            [Sin categoría]
            LangSecRef=9999
            FileKey1=C:\X|*.tmp

            [Sin rutas]
            LangSecRef=3024
            """);

        var entries = Winapp2.Parse(ini);

        Assert.Equal(["Programa de prueba", "Juego"], entries.Select(e => e.Name));
        var p = entries[0];
        Assert.Equal("Aplicaciones", p.Category);
        Assert.Single(p.FileKeys);
        Assert.Single(p.ExcludeKeys);
        Assert.Equal("Cuidado", p.Warning);
        Assert.Equal("Juegos", entries[1].Category);
    }

    [Theory]
    [InlineData("cache_data", "cache*", true)]
    [InlineData("Cache_Data", "cache*", true)]
    [InlineData("a.log", "*.log", true)]
    [InlineData("a.log.bak", "*.log", false)]
    [InlineData("v1", "v?", true)]
    [InlineData("v10", "v?", false)]
    public void Los_comodines_funcionan_como_en_windows(string text, string pattern, bool expected) =>
        Assert.Equal(expected, Winapp2.WildcardMatch(text, pattern));

    [Fact]
    public void Expande_comodines_en_cada_tramo_de_la_ruta()
    {
        using var t = new TempDir();
        Directory.CreateDirectory(Path.Combine(t.Path, "Perfil1", "Cache"));
        Directory.CreateDirectory(Path.Combine(t.Path, "Perfil2", "Cache"));
        Directory.CreateDirectory(Path.Combine(t.Path, "Otro", "Datos"));

        var dirs = Winapp2.ExpandDirs(Path.Combine(t.Path, "Perfil*", "Cache")).ToList();

        Assert.Equal(2, dirs.Count);
        Assert.All(dirs, d => Assert.EndsWith("Cache", d));
    }

    [Fact]
    public void Una_variable_desconocida_no_se_limpia()
    {
        Assert.Empty(Winapp2.ExpandDirs(@"%NoExisteEstaVariable%\Cache"));
    }

    [Fact]
    public void Convierte_FileKey_y_respeta_las_exclusiones()
    {
        using var t = new TempDir();
        var cache = Path.Combine(t.Path, "Cache");
        var keep = t.File(@"Cache\config.json");
        var junk = t.File(@"Cache\data_1.tmp");
        var entry = new Winapp2.Entry { Name = "Prueba", Category = "Utilidades" };
        entry.FileKeys.Add($"{cache}|*.tmp;*.json|REMOVESELF");
        entry.ExcludeKeys.Add($@"FILE|{cache}\|config.json");

        var specs = Winapp2.Specs(entry).ToList();

        Assert.Equal(2, specs.Count);
        Assert.All(specs, s => { Assert.True(s.Recursive); Assert.True(s.RemoveSelf); Assert.Equal(cache, s.Dir); });
        Assert.True(Winapp2.IsExcluded(entry, keep));
        Assert.False(Winapp2.IsExcluded(entry, junk));
    }

    [Fact]
    public void Nunca_vacia_una_carpeta_protegida()
    {
        var entry = new Winapp2.Entry { Name = "Peligrosa", Category = "Utilidades" };
        entry.FileKeys.Add(@"%UserProfile%|*.*|RECURSE");
        entry.FileKeys.Add(@"%WinDir%|*.*");
        entry.FileKeys.Add(@"%AppData%|*|REMOVESELF");

        Assert.Empty(Winapp2.Specs(entry));
    }
}

public class TreemapTests
{
    [Fact]
    public void Las_areas_son_proporcionales_y_caben_en_el_rectangulo()
    {
        var area = new Rect(0, 0, 400, 300);
        var items = new[] { ("a", 50.0), ("b", 25.0), ("c", 15.0), ("d", 10.0) };

        var layout = Treemap.Layout(items, area);

        Assert.Equal(4, layout.Count);
        var total = items.Sum(i => i.Item2);
        foreach (var (item, rect) in layout)
        {
            var expected = items.First(i => i.Item1 == item).Item2 / total * area.Width * area.Height;
            Assert.InRange(rect.Width * rect.Height, expected * 0.99, expected * 1.01);
            Assert.True(rect.Left >= -0.01 && rect.Top >= -0.01 && rect.Right <= area.Width + 0.01 && rect.Bottom <= area.Height + 0.01);
        }
    }

    [Fact]
    public void Ignora_elementos_vacios()
    {
        var layout = Treemap.Layout([("a", 10.0), ("cero", 0.0)], new Rect(0, 0, 100, 100));
        Assert.Equal("a", Assert.Single(layout).item);
    }
}

public class LeftoverMatchTests
{
    private static readonly List<string> Dirs = [@"C:\Program Files\Zoom"];

    [Theory]
    [InlineData(@"""C:\Program Files\Zoom\bin\Zoom.exe"" --service", true)]
    [InlineData(@"C:\Program Files\Zoom", true)]
    [InlineData(@"""C:\Program Files\Zoom""", true)]
    [InlineData(@"C:\Program Files\ZoomIt\zoomit.exe", false)]   // otra carpeta que empieza igual
    [InlineData(@"C:\Windows\System32\svchost.exe -k netsvcs", false)]
    [InlineData(null, false)]
    public void Solo_coincide_si_apunta_a_la_carpeta_del_programa(string? text, bool expected) =>
        Assert.Equal(expected, UninstallerService.PointsTo(text, Dirs));
}

public class PrivacyTests
{
    [Fact]
    public void Aplicar_y_restaurar_deja_el_valor_original()
    {
        const string path = @"Software\OpenSweepTests\Privacy";
        using (var k = Registry.CurrentUser.CreateSubKey(path)) { k.SetValue("Existente", 7, RegistryValueKind.DWord); k.DeleteValue("Nuevo", false); }
        try
        {
            var s = new PrivacySetting
            {
                Id = "test", Title = "Prueba", Description = "",
                Values = [new(RegistryHive.CurrentUser, path, "Existente", 0), new(RegistryHive.CurrentUser, path, "Nuevo", 1)],
            };

            Assert.True(PrivacyService.Set(s, true));
            using (var k = Registry.CurrentUser.OpenSubKey(path)!)
            {
                Assert.Equal(0, k.GetValue("Existente"));
                Assert.Equal(1, k.GetValue("Nuevo"));
            }

            Assert.True(PrivacyService.Set(s, false));
            using (var k = Registry.CurrentUser.OpenSubKey(path)!)
            {
                Assert.Equal(7, k.GetValue("Existente"));   // vuelve al valor que había
                Assert.Null(k.GetValue("Nuevo"));            // no existía: se borra
            }
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(@"Software\OpenSweepTests", false); }
    }

    [Fact]
    public void Todos_los_ajustes_tienen_textos_y_valores()
    {
        var all = PrivacyService.Settings();
        Assert.Equal(all.Count, all.Select(s => s.Id).Distinct().Count());
        Assert.All(all, s => { Assert.NotEmpty(s.Title); Assert.NotEmpty(s.Description); Assert.NotEmpty(s.Values); });
    }
}
