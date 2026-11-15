using Microsoft.Win32;
using OpenSweep.Services;

namespace OpenSweep.Tests;

public class UninstallerTracesTests
{
    [Theory]
    [InlineData(@"C:\Program Files\App\app.exe.FriendlyAppName", @"C:\Program Files\App\app.exe")]
    [InlineData(@"C:\Program Files\App\app.exe.ApplicationCompany", @"C:\Program Files\App\app.exe")]
    [InlineData(@"C:\Program Files\App\app.exe", @"C:\Program Files\App\app.exe")]
    public void Extrae_la_ruta_de_los_valores_de_MuiCache(string value, string expected) =>
        Assert.Equal(expected, UninstallerService.TraceValuePath(value));

    [Fact]
    public void Calcula_el_nombre_de_Prefetch_como_Windows()
    {
        // Windows recorta a 29 caracteres: «TIKTOK LIVE STUDIO UNINSTALLE-AE862CF2.pf»
        var heads = UninstallerService.PrefetchHeads(["TikTok LIVE Studio Uninstaller", "app"]);
        Assert.Contains("TIKTOK LIVE STUDIO UNINSTALLE", heads);
        Assert.Contains("APP.EXE", heads);
    }

    [Fact]
    public void Una_palabra_comun_no_marca_restos_de_otros_programas()
    {
        // «TikTok LIVE Studio»: «Studio» no debe arrastrar a OBS Studio, Visual Studio Code, etc.
        using var t = new TempDir();
        var program = UninstallerService.FakeProgram("Programa Prueba Studio", t.Path);
        var leftovers = UninstallerService.FindLeftovers(program, ScanMode.Avanzado, CancellationToken.None);
        Assert.DoesNotContain(leftovers, l => l.Selected && l.Path.Contains("Visual Studio", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(leftovers, l => l.Selected && l.Path.Contains("obs-studio", StringComparison.OrdinalIgnoreCase));
    }
}

public class LeftoverTreeTests
{
    private static Leftover Value(string key, string name) =>
        new() { Path = key, IsRegistry = true, Hive = RegistryHive.CurrentUser, View = RegistryView.Registry64, ValueName = name };

    [Fact]
    public void Agrupa_los_valores_bajo_su_clave_y_las_claves_bajo_su_raiz()
    {
        var roots = LeftoverTree.BuildRegistry(
        [
            Value(@"Software\A", "uno"), Value(@"Software\A", "dos"),
            new Leftover { Path = @"SOFTWARE\B", IsRegistry = true, Hive = RegistryHive.LocalMachine, View = RegistryView.Registry64 },
        ]);
        Assert.Equal(2, roots.Count);                                  // HKCU y HKLM
        var key = Assert.Single(roots[0].Children);
        Assert.Equal(2, key.Children.Count);                           // dos valores dentro de Software\A
        Assert.Null(Assert.Single(roots[1].Children).Children.FirstOrDefault());   // la clave B es una hoja
    }

    [Fact]
    public void Marcar_un_grupo_marca_sus_hijos_y_un_hijo_deja_el_grupo_a_medias()
    {
        var a = Value(@"Software\A", "uno"); a.Selected = false;
        var b = Value(@"Software\A", "dos"); b.Selected = false;
        var root = LeftoverTree.BuildRegistry([a, b])[0];
        Assert.False(root.IsChecked);

        root.IsChecked = true;
        Assert.True(a.Selected && b.Selected);

        root.Children[0].Children[0].IsChecked = false;               // desmarcar un valor
        Assert.Null(root.IsChecked);                                  // el grupo queda a medias
        Assert.Equal(1, root.Items().Count(i => i.Selected));
    }

    [Fact]
    public void Los_archivos_se_agrupan_por_carpeta()
    {
        var roots = LeftoverTree.BuildFiles(
        [
            new Leftover { Path = @"C:\Temp\x\uno", IsRegistry = false, IsDirectory = true, Size = 10 },
            new Leftover { Path = @"C:\Temp\x\dos.txt", IsRegistry = false, Size = 5 },
            new Leftover { Path = @"C:\Otra\tres", IsRegistry = false, IsDirectory = true },
        ]);
        Assert.Equal(2, roots.Count);
        Assert.Equal(2, roots.Single(r => r.Text == @"C:\Temp\x").Children.Count);
    }
}
