using Microsoft.Win32;
using OpenSweep.Core;
using OpenSweep.Services;

namespace OpenSweep.Tests;

/// <summary>Protecciones añadidas en la revisión general: nada de esto debe poder borrar datos del usuario.</summary>
public class SafetyTests
{
    [Fact]
    public void Las_carpetas_personales_estan_protegidas()
    {
        foreach (var f in new[] { Environment.SpecialFolder.MyPictures, Environment.SpecialFolder.MyMusic,
                     Environment.SpecialFolder.MyVideos, Environment.SpecialFolder.MyDocuments, Environment.SpecialFolder.DesktopDirectory })
        {
            var path = Environment.GetFolderPath(f);
            if (path.Length > 0) Assert.True(PathHelper.IsProtectedFolder(path), path);
        }
    }

    [Fact]
    public void Un_comodin_justo_debajo_de_una_carpeta_protegida_se_detecta()
    {
        Assert.True(Winapp2.WildcardUnderProtected(@"%LocalAppData%\*"));
        Assert.True(Winapp2.WildcardUnderProtected(@"%AppData%\*\Cache"));
        Assert.False(Winapp2.WildcardUnderProtected(@"%LocalAppData%\Programa\*"));
        Assert.False(Winapp2.WildcardUnderProtected(@"%LocalAppData%\Programa\Cache"));
    }

    [Fact]
    public void Winapp2_no_toca_la_raiz_de_imagenes_ni_con_un_patron_concreto()
    {
        var entry = new Winapp2.Entry { Name = "Peligrosa", Category = "Utilidades" };
        entry.FileKeys.Add(@"%Pictures%|*.jpg");
        entry.FileKeys.Add(@"%LocalAppData%\*|*.*|RECURSE");
        Assert.Empty(Winapp2.Specs(entry));
    }

    [Fact]
    public void La_papelera_y_las_carpetas_propias_son_reglas_delicadas_en_la_linea_de_comandos()
    {
        CleanRule R(string id) => new() { Id = id, Group = "Windows", Section = "S", Name = id, DefaultOn = true, Selected = true, Paths = () => [] };
        var all = new List<CleanRule> { R("win.usertemp"), R("win.recycle"), R("custom.D:\\Fotos") };
        Assert.Equal(["win.usertemp"], CommandLine.SelectRules(all, "all", false).Select(r => r.Id));
        Assert.Equal(["win.recycle"], CommandLine.SelectRules(all, "win.recycle", false).Select(r => r.Id));   // nombrada: sí
    }

    [Fact]
    public void La_copia_de_un_valor_guarda_solo_ese_valor_con_su_tipo()
    {
        const string key = @"Software\OpenSweepTestsValores";
        using (var k = Registry.CurrentUser.CreateSubKey(key))
        {
            k.SetValue("Borrar", 5, RegistryValueKind.DWord);
            k.SetValue("Otro", "no debe aparecer");
            k.SetValue("Ruta", @"%SystemRoot%\x.exe", RegistryValueKind.ExpandString);
        }
        try
        {
            var reg = RegistryCleaner.ValuesToReg(RegistryHive.CurrentUser, RegistryView.Registry64, key, ["Borrar", "Ruta"]);
            Assert.Contains(@"[HKEY_CURRENT_USER\Software\OpenSweepTestsValores]", reg);
            Assert.Contains("\"Borrar\"=dword:00000005", reg);
            Assert.Contains("\"Ruta\"=hex(2):", reg);
            Assert.DoesNotContain("Otro", reg);
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(key, false); }
    }

    [Fact]
    public void Las_claves_de_32_bits_se_copian_con_su_ruta_real()
    {
        if (!Environment.Is64BitOperatingSystem) return;
        Assert.Equal(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Programa",
            RegistryCleaner.RealKeyPath(RegistryHive.LocalMachine, RegistryView.Registry32, @"SOFTWARE\Programa"));
        Assert.Equal(@"HKEY_LOCAL_MACHINE\SOFTWARE\Programa",
            RegistryCleaner.RealKeyPath(RegistryHive.LocalMachine, RegistryView.Registry64, @"SOFTWARE\Programa"));
    }

    [Fact]
    public void Destruir_un_enlace_no_toca_el_archivo_de_destino()
    {
        using var t = new TempDir();
        var target = t.File("destino.txt", "datos importantes");
        var link = Path.Combine(t.Path, "enlace.txt");
        try { File.CreateSymbolicLink(link, target); }
        catch (Exception) { return; }   // sin permiso para crear enlaces en este equipo: no se puede probar
        Assert.True(SecureWipe.ShredFile(link, 1));
        Assert.False(File.Exists(link));
        Assert.Equal("datos importantes", File.ReadAllText(target));
    }
}
