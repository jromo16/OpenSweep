using OpenSweep.Services;

namespace OpenSweep.Tests;

public class CommandLineTests
{
    private static CleanRule Rule(string id, string group, bool defaultOn = true, bool selected = true) =>
        new() { Id = id, Group = group, Section = "S", Name = id, DefaultOn = defaultOn, Selected = selected, Paths = () => [] };

    private static readonly List<CleanRule> All =
    [
        Rule("win.usertemp", "Windows"),
        Rule("br.chrome.cache", "Navegadores"),
        Rule("br.chrome.cookies", "Navegadores", defaultOn: false, selected: false),
        Rule("priv.clipboard", "Privacidad"),
    ];

    [Fact]
    public void Un_grupo_no_incluye_cookies_ni_historial()
    {
        var r = CommandLine.SelectRules(All, "group:Navegadores", includePrivacy: false);
        Assert.Equal(["br.chrome.cache"], r.Select(x => x.Id));
    }

    [Fact]
    public void All_no_incluye_lo_delicado_salvo_que_se_pida()
    {
        Assert.Equal(["win.usertemp", "br.chrome.cache"], CommandLine.SelectRules(All, "all", false).Select(x => x.Id));
        Assert.Equal(4, CommandLine.SelectRules(All, "all", true).Count);
    }

    [Fact]
    public void Nombrar_una_regla_la_incluye_aunque_sea_delicada() =>
        Assert.Equal(["br.chrome.cookies"], CommandLine.SelectRules(All, "br.chrome.cookies", false).Select(x => x.Id));

    [Fact]
    public void Sin_rules_usa_lo_marcado_sin_privacidad() =>
        Assert.Equal(["win.usertemp", "br.chrome.cache"], CommandLine.SelectRules(All, null, false).Select(x => x.Id));
}

public class PortableUpdateTests
{
    [Fact]
    public void Lee_la_huella_sha256_que_publica_GitHub()
    {
        using var doc = System.Text.Json.JsonDocument.Parse("""
            {"tag_name":"v9.0.0","html_url":"https://github.com/jromo16/OpenSweep/releases/tag/v9.0.0","assets":[
              {"name":"OpenSweep-Setup.exe","browser_download_url":"https://github.com/jromo16/OpenSweep/releases/download/v9.0.0/OpenSweep-Setup.exe","digest":"sha256:ABC123"},
              {"name":"OpenSweep-windows.exe","browser_download_url":"https://github.com/jromo16/OpenSweep/releases/download/v9.0.0/OpenSweep-windows.exe"}]}
            """);
        var u = AppUpdater.Parse(doc.RootElement, new Version(1, 3, 0));
        Assert.NotNull(u);
        Assert.Equal("ABC123", u!.SetupSha256);
        Assert.Null(u.PortableSha256);
        Assert.Null(AppUpdater.Parse(doc.RootElement, new Version(9, 0, 0)));   // misma versión: nada que hacer
    }

    [Fact]
    public void Rechaza_un_archivo_alterado_o_incompleto()
    {
        using var t = new TempDir();
        var file = t.File("OpenSweep.exe", "MZ contenido del ejecutable");
        var sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file)));
        Assert.True(AppUpdater.Verify(file, new FileInfo(file).Length, sha));
        Assert.False(AppUpdater.Verify(file, new FileInfo(file).Length, new string('0', 64)));   // otra huella
        Assert.False(AppUpdater.Verify(file, 999_999, null));                                     // descarga cortada
        Assert.False(AppUpdater.Verify(t.File("x.exe", "no es un exe"), 0, null));
    }
}
