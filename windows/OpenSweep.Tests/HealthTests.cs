using OpenSweep.Services;

namespace OpenSweep.Tests;

public class BrowserWatcherTests
{
    [Fact]
    public void Detecta_solo_los_navegadores_que_se_cerraron()
    {
        var closed = BrowserWatcher.Closed(["Google Chrome", "Mozilla Firefox"], ["Mozilla Firefox", "Brave"]);
        Assert.Equal(["Google Chrome"], closed);
    }

    [Fact]
    public void Sin_cambios_no_limpia_nada() =>
        Assert.Empty(BrowserWatcher.Closed(["Brave"], ["brave"]));
}

public class DiskHealthTests
{
    [Fact]
    public void Lee_la_salida_de_PowerShell_con_uno_o_varios_discos()
    {
        var one = DiskHealth.Parse("""{"Name":"SSD","Media":"SSD","Bus":"NVMe","Size":1000,"Health":"Healthy","Temperature":40,"Wear":3,"ReadErrors":0,"WriteErrors":0,"PowerOnHours":1200}""");
        var d = Assert.Single(one);
        Assert.Equal(40, d.Temperature);
        Assert.Equal(DiskState.Good, d.State);

        var many = DiskHealth.Parse("""[{"Name":"A","Size":1,"Health":"Healthy"},{"Name":"B","Size":2,"Health":"Unhealthy"},{"Name":"Vacío","Size":0}]""");
        Assert.Equal(2, many.Count);
        Assert.Equal(DiskState.Bad, many[1].State);
    }

    [Fact]
    public void Salida_vacia_o_rota_no_falla()
    {
        Assert.Empty(DiskHealth.Parse(""));
        Assert.Empty(DiskHealth.Parse("error de PowerShell"));
    }

    [Theory]
    [InlineData("Healthy", null, null, null, DiskState.Good)]
    [InlineData("Healthy", 95, null, null, DiskState.Warning)]      // SSD gastado
    [InlineData("Healthy", 10, 3L, null, DiskState.Warning)]        // errores de lectura sin corregir
    [InlineData("Healthy", null, null, 70, DiskState.Warning)]      // muy caliente
    [InlineData("Warning", null, null, null, DiskState.Warning)]
    [InlineData("Unhealthy", null, null, null, DiskState.Bad)]
    [InlineData("", null, null, null, DiskState.Unknown)]
    public void Interpreta_el_estado(string health, int? wear, long? readErrors, int? temp, DiskState expected) =>
        Assert.Equal(expected, new DiskHealthInfo { Health = health, Wear = wear, ReadErrors = readErrors, Temperature = temp, Size = 1 }.State);
}

public class WindowsRepairTests
{
    [Theory]
    [InlineData("[=====                      10.0%                          ]", 10.0)]
    [InlineData("[==========================100.0%==========================] ", 100.0)]
    [InlineData("Verificación 45% completada.", 45.0)]
    [InlineData("Verification 7% complete.\rVerification 8% complete.", 8.0)]
    [InlineData("Comprobación 62,5% completada", 62.5)]
    public void Lee_el_porcentaje_de_DISM_y_SFC(string text, double expected) =>
        Assert.Equal(expected, WindowsRepair.LastPercent(text));

    [Fact]
    public void Sin_porcentaje_devuelve_null() =>
        Assert.Null(WindowsRepair.LastPercent("Iniciando la exploración del sistema."));
}
