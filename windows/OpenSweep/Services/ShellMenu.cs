using Microsoft.Win32;
using OpenSweep.Core;

namespace OpenSweep.Services;

/// <summary>
/// Opciones de OpenSweep en el menú del clic derecho del Explorador (como Revo y CCleaner):
/// «Desinstalar con OpenSweep» en accesos directos y programas, y «Destruir con OpenSweep» en archivos y carpetas.
/// Se registran solo para el usuario actual (HKCU\Software\Classes), así que no tocan la configuración de otros usuarios.
/// En Windows 11 aparecen en «Mostrar más opciones».
/// </summary>
public static class ShellMenu
{
    private const string Classes = @"Software\Classes";

    /// <summary>(tipo de elemento, nombre del verbo, texto, argumento de OpenSweep)</summary>
    private static readonly (string type, string verb, string text, string arg)[] Verbs =
    [
        ("lnkfile", "OpenSweep.Uninstall", "Desinstalar con OpenSweep", "--uninstall"),
        ("exefile", "OpenSweep.Uninstall", "Desinstalar con OpenSweep", "--uninstall"),
        ("*", "OpenSweep.Shred", "Destruir con OpenSweep (irrecuperable)", "--shred"),
        ("Directory", "OpenSweep.Shred", "Destruir con OpenSweep (irrecuperable)", "--shred"),
    ];

    private static string Exe => Environment.ProcessPath ?? "";

    public static bool IsEnabled
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey($@"{Classes}\lnkfile\shell\OpenSweep.Uninstall\command");
            return k != null;
        }
    }

    /// <summary>¿Las entradas apuntan a otro OpenSweep.exe (p. ej. se movió el portable)?</summary>
    public static bool PointsElsewhere
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey($@"{Classes}\lnkfile\shell\OpenSweep.Uninstall\command");
            return k?.GetValue("") is string cmd && !cmd.Contains(Exe, StringComparison.OrdinalIgnoreCase);
        }
    }

    public static string Command(string arg) => $"\"{Exe}\" {arg} \"%1\"";

    public static bool Enable()
    {
        try
        {
            foreach (var (type, verb, text, arg) in Verbs)
            {
                using var k = Registry.CurrentUser.CreateSubKey($@"{Classes}\{type}\shell\{verb}");
                k.SetValue("MUIVerb", Loc.T(text));
                k.SetValue("Icon", $"\"{Exe}\",0");
                // Solo con un elemento seleccionado: Windows abriría una ventana (y un aviso de permisos) por cada archivo
                k.SetValue("MultiSelectModel", "Single");
                using var c = k.CreateSubKey("command");
                c.SetValue("", Command(arg));
            }
            return true;
        }
        catch { return false; }
    }

    public static void Disable()
    {
        foreach (var (type, verb, _, _) in Verbs)
        {
            try { Registry.CurrentUser.DeleteSubKeyTree($@"{Classes}\{type}\shell\{verb}", false); } catch { }
        }
    }
}
