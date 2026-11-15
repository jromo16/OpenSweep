using System.Text;
using Microsoft.Win32;
using OpenSweep.Core;
using OpenSweep.Services;

namespace OpenSweep.Tests;

public class RecycleBinTests
{
    [Fact]
    public void Lee_el_formato_de_Windows_10()
    {
        const string path = @"C:\Program Files\App\config.ini";
        var bytes = new List<byte>();
        bytes.AddRange(BitConverter.GetBytes(2L));                                   // versión
        bytes.AddRange(BitConverter.GetBytes(1234L));                                // tamaño
        bytes.AddRange(BitConverter.GetBytes(new DateTime(2026, 11, 15).ToFileTime())); // fecha
        bytes.AddRange(BitConverter.GetBytes(path.Length + 1));                      // caracteres con el \0
        bytes.AddRange(Encoding.Unicode.GetBytes(path + "\0"));

        var info = RecycleBin.ReadInfo([.. bytes]);

        Assert.NotNull(info);
        Assert.Equal(path, info!.Value.path);
        Assert.Equal(new DateTime(2026, 11, 15), info.Value.deleted);
    }

    [Fact]
    public void Restaura_un_archivo_enviado_a_la_papelera()
    {
        using var t = new TempDir();
        var file = t.File(@"restos\config.ini", "contenido original");
        Assert.True(Native.SendToRecycleBin(file));
        Assert.False(File.Exists(file));

        var restored = RecycleBin.Restore([file]);

        Assert.Equal([file], restored);
        Assert.Equal("contenido original", File.ReadAllText(file));
    }
}

public class UndoUninstallTests
{
    [Fact]
    public void Deshacer_devuelve_el_registro_y_los_archivos()
    {
        const string key = @"Software\OpenSweepTestsDeshacer\ProgramaDeshacer";
        using var t = new TempDir();
        var folder = Path.Combine(t.Path, "ProgramaDeshacer");
        t.File(@"ProgramaDeshacer\datos.txt", "datos");
        using (var k = Registry.CurrentUser.CreateSubKey(key)) k.SetValue("Idioma", "es");
        UninstallRecord? record = null;
        try
        {
            var items = new List<Leftover>
            {
                new() { Path = key, IsRegistry = true, Hive = RegistryHive.CurrentUser, View = RegistryView.Registry64 },
                new() { Path = folder, IsRegistry = false, IsDirectory = true },
            };
            // Lo mismo que hace el asistente al pulsar «Eliminar»
            var (regFile, _) = UninstallerService.BackupRegistryLeftovers(items);
            Assert.All(items, i => Assert.True(UninstallerService.DeleteLeftover(i)));
            Assert.Null(Registry.CurrentUser.OpenSubKey(key));
            Assert.False(Directory.Exists(folder));
            record = new UninstallRecord { Program = "Programa de prueba", RegFiles = [regFile!], Recycled = [folder], RegistryItems = 1 };

            var (ok, failed, _) = UninstallBackups.Restore(record);

            Assert.Equal(0, failed);
            Assert.Equal(2, ok);
            using (var k = Registry.CurrentUser.OpenSubKey(key)) Assert.Equal("es", k?.GetValue("Idioma"));
            Assert.Equal("datos", File.ReadAllText(Path.Combine(folder, "datos.txt")));
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\OpenSweepTestsDeshacer", false);
            if (record != null)
            {
                UninstallBackups.Delete(record);
                foreach (var f in record.RegFiles) try { File.Delete(f); } catch { }
            }
        }
    }
}

public class AutoTraceTests
{
    [Theory]
    [InlineData("OBS-Studio-32.0-Windows-Installer", true)]
    [InlineData("ChromeSetup", true)]
    [InlineData("vlc-3.0.21-win64.tmp-setup", true)]
    [InlineData("unins000", false)]
    [InlineData("Uninstall", false)]
    [InlineData("chrome", false)]
    [InlineData("OpenSweep-Setup", false)]
    public void Reconoce_los_instaladores(string process, bool expected) =>
        Assert.Equal(expected, AutoTrace.LooksLikeInstaller(process));

    [Fact]
    public void Solo_guarda_lo_creado_desde_que_empezo_la_instalacion()
    {
        using var t = new TempDir();
        var vieja = Path.Combine(t.Path, "vieja");
        var nueva = Path.Combine(t.Path, "nueva");
        Directory.CreateDirectory(vieja);
        Directory.CreateDirectory(nueva);
        Directory.SetCreationTime(vieja, DateTime.Now.AddHours(-3));   // de otro programa, antes de instalar
        var trace = new InstallTrace { Items = [new TraceItem { Kind = "dir", Path = vieja }, new TraceItem { Kind = "dir", Path = nueva }] };

        AutoTrace.KeepSince(trace, DateTime.Now.AddMinutes(-5));

        Assert.Equal(nueva, Assert.Single(trace.Items).Path);
    }

    [Fact]
    public void Lee_la_fecha_de_modificacion_de_una_clave()
    {
        using (Registry.CurrentUser.CreateSubKey(@"Software\OpenSweepTestsFecha\Fecha")) { }
        try
        {
            var t = AutoTrace.KeyLastWrite("HKCU", "64", @"Software\OpenSweepTestsFecha\Fecha");
            Assert.NotNull(t);
            Assert.InRange(t!.Value, DateTime.Now.AddMinutes(-1), DateTime.Now.AddMinutes(1));
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(@"Software\OpenSweepTestsFecha", false); }
    }
}
