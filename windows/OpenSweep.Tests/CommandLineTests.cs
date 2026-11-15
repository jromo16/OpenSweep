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
    public void El_script_portable_espera_reemplaza_y_vuelve_a_abrir()
    {
        var s = AppUpdater.PortableSwapScript(1234, @"C:\Apps\OpenSweep.exe.new", @"C:\Apps\OpenSweep.exe");
        Assert.Contains("PID eq 1234", s);
        Assert.Contains("""move /Y "C:\Apps\OpenSweep.exe.new" "C:\Apps\OpenSweep.exe" """.TrimEnd(), s);
        Assert.Contains("""start "" "C:\Apps\OpenSweep.exe" """.TrimEnd(), s);
    }
}
