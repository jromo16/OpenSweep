using OpenSweep.Core;
using OpenSweep.Services;

namespace OpenSweep.Tests;

public class CleanerTests
{
    private static CleanRule Rule(string dir, int minAgeHours = 0) => new()
    {
        Id = "test", Group = "Prueba", Section = "Prueba", Name = "Prueba",
        Paths = () => [new PathSpec(dir, "*", true, minAgeHours)],
    };

    [Fact]
    public void Analizar_cuenta_archivos_y_tamaño_sin_borrar()
    {
        using var t = new TempDir();
        t.File("a.tmp", new string('x', 100));
        t.File("sub\\b.tmp", new string('x', 50));

        var r = JunkCleaner.Analyze(Rule(t.Path), false, CancellationToken.None);

        Assert.Equal(2, r.Count);
        Assert.Equal(150, r.Bytes);
        Assert.True(File.Exists(Path.Combine(t.Path, "a.tmp")));   // analizar nunca borra
    }

    [Fact]
    public void Limpiar_respeta_la_antiguedad_minima()
    {
        using var t = new TempDir();
        var viejo = t.File("viejo.tmp", "x", DateTime.Now.AddDays(-3));
        var nuevo = t.File("nuevo.tmp", "x", DateTime.Now);

        var r = JunkCleaner.Clean(Rule(t.Path, minAgeHours: 24), CancellationToken.None);

        Assert.Equal(1, r.Count);
        Assert.False(File.Exists(viejo));
        Assert.True(File.Exists(nuevo));
    }

    [Fact]
    public void Limpiar_respeta_las_exclusiones()
    {
        using var t = new TempDir();
        var protegido = t.File("conservar\\importante.tmp");
        var basura = t.File("basura.tmp");
        AppSettings.Current.Exclusions.Add(Path.Combine(t.Path, "conservar"));
        try
        {
            JunkCleaner.Clean(Rule(t.Path), CancellationToken.None);
            Assert.True(File.Exists(protegido));
            Assert.False(File.Exists(basura));
        }
        finally
        {
            AppSettings.Current.Exclusions.Remove(Path.Combine(t.Path, "conservar"));
        }
    }

    [Fact]
    public void Limpiar_quita_carpetas_vacias_pero_no_la_raiz()
    {
        using var t = new TempDir();
        t.File("a\\b\\c.tmp");

        JunkCleaner.Clean(Rule(t.Path), CancellationToken.None);

        Assert.False(Directory.Exists(Path.Combine(t.Path, "a")));
        Assert.True(Directory.Exists(t.Path));
    }

    [Fact]
    public void Las_reglas_delicadas_vienen_desmarcadas()
    {
        var rules = JunkCleaner.BuildRules();
        // Se borran solo si el usuario las marca
        Assert.All(rules.Where(r => r.Name.StartsWith("Cookies") || r.Name == "Historial de navegación"), r => Assert.False(r.DefaultOn));
        Assert.All(rules.Where(r => r.Section == "Actualizaciones de Windows"), r => Assert.False(r.DefaultOn));
        Assert.DoesNotContain(rules, r => r.Section == "Microsoft Office");
    }
}
