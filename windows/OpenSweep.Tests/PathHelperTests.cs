using OpenSweep.Core;

namespace OpenSweep.Tests;

public class PathHelperTests
{
    [Theory]
    [InlineData("\"C:\\Program Files\\App\\uninstall.exe\" /S", "C:\\Program Files\\App\\uninstall.exe")]
    [InlineData("C:\\Program Files\\App\\app.exe,0", "C:\\Program Files\\App\\app.exe")]
    [InlineData("C:\\App\\unins000.exe /SILENT", "C:\\App\\unins000.exe")]
    [InlineData("rundll32.exe C:\\App\\lib.dll,Uninstall", "C:\\App\\lib.dll")]
    public void ExtractPath_obtiene_el_ejecutable(string commandLine, string expected) =>
        Assert.Equal(expected, PathHelper.ExtractPath(commandLine));

    [Fact]
    public void ExtractPath_expande_variables_de_entorno()
    {
        var path = PathHelper.ExtractPath("%SystemRoot%\\notepad.exe");
        Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "notepad.exe"), path, ignoreCase: true);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ExtractPath_vacio_devuelve_null(string? commandLine) => Assert.Null(PathHelper.ExtractPath(commandLine));

    [Fact]
    public void Carpetas_del_sistema_estan_protegidas()
    {
        Assert.True(PathHelper.IsProtectedFolder(@"C:\"));
        Assert.True(PathHelper.IsProtectedFolder(Environment.GetFolderPath(Environment.SpecialFolder.Windows)));
        Assert.True(PathHelper.IsProtectedFolder(Environment.SystemDirectory));
        Assert.True(PathHelper.IsProtectedFolder(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));
        Assert.True(PathHelper.IsProtectedFolder(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)));
        Assert.True(PathHelper.IsProtectedFolder(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)));
    }

    [Fact]
    public void Una_carpeta_que_contiene_carpetas_del_sistema_esta_protegida()
    {
        // C:\Users contiene los perfiles de usuario: nunca se puede eliminar completa
        Assert.True(PathHelper.IsProtectedFolder(Path.GetDirectoryName(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))!));
    }

    [Fact]
    public void Una_carpeta_normal_no_esta_protegida()
    {
        using var t = new TempDir();
        Assert.False(PathHelper.IsProtectedFolder(t.Path));
    }

    [Fact]
    public void Archivo_inexistente_se_detecta()
    {
        using var t = new TempDir();
        Assert.False(PathHelper.FileExistsSmart(Path.Combine(t.Path, "no-existe.exe")));
        Assert.True(PathHelper.FileExistsSmart(t.File("existe.exe")));
    }

    [Fact]
    public void Unidad_no_conectada_no_se_marca_como_problema()
    {
        // Un USB desconectado no significa que el programa ya no exista
        var missingDrive = Enumerable.Range('D', 22).Select(c => $"{(char)c}:\\").First(d => !Directory.Exists(d));
        Assert.True(PathHelper.FileExistsSmart(missingDrive + "Programa\\app.exe"));
    }
}
