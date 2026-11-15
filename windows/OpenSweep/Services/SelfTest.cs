using System.Diagnostics;
using System.Text;
using Microsoft.Win32;
using OpenSweep.Core;

namespace OpenSweep.Services;

/// <summary>
/// Prueba completa de las funciones que borran o cambian cosas, pensada para Windows Sandbox.
/// Instala un programa falso con restos de todo tipo, lo desinstala, borra los restos, limpia,
/// repara el registro, aplica y restaura la privacidad y crea y quita las tareas programadas.
/// Uso: OpenSweep.exe --selftest C:\Resultados\informe.txt [--full]
/// </summary>
public static class SelfTest
{
    public const string SandboxUser = "WDAGUtilityAccount";
    private const string AppName = "OpenSweep Prueba";
    private const string KeyName = "OpenSweepPrueba";
    private const string Service = "OpenSweepPruebaSvc";
    private const string FwRule = "OpenSweep Prueba";

    /// <summary>Solo dentro de Windows Sandbox: en un PC real cambiaría cosas de verdad.</summary>
    public static bool IsSandbox => Environment.UserName.Equals(SandboxUser, StringComparison.OrdinalIgnoreCase);

    private static readonly StringBuilder Report = new();
    private static int _ok, _fail, _warn;

    private static void Line(string state, string step, string detail)
    {
        if (state == "OK") _ok++; else if (state == "FALLO") _fail++; else _warn++;
        Report.AppendLine($"[{state,-5}] {step}: {detail}");
    }

    private static void Check(bool ok, string step, string detail) => Line(ok ? "OK" : "FALLO", step, detail);

    private static void Section(string title) => Report.AppendLine().AppendLine($"== {title} ==");

    /// <summary>Devuelve el número de fallos (código de salida).</summary>
    public static async Task<int> RunAsync(string reportPath, bool full)
    {
        var sw = Stopwatch.StartNew();
        Report.AppendLine($"OpenSweep {System.Reflection.Assembly.GetExecutingAssembly().GetName().Version} · prueba completa · {DateTime.Now:yyyy-MM-dd HH:mm}");
        Report.AppendLine($"Windows {Environment.OSVersion.Version} · usuario {Environment.UserName}");

        if (!IsSandbox)
        {
            Line("FALLO", "Entorno", "No es Windows Sandbox. La prueba se cancela para no cambiar nada en este PC.");
            return Finish(reportPath, sw);
        }

        await Step("Desinstalador y monitor de instalaciones", UninstallTest);
        await Step("Limpieza", () => { CleanTest(); return Task.CompletedTask; });
        await Step("Registro", () => { RegistryTest(); return Task.CompletedTask; });
        await Step("Privacidad", () => { PrivacyTest(); return Task.CompletedTask; });
        await Step("Tareas programadas", () => { SchedulerTest(); return Task.CompletedTask; });
        await Step("Reglas de la comunidad (winapp2)", Winapp2Test);
        if (full) await Step("Liberador de espacio y componentes de Windows", () => { SystemTest(); return Task.CompletedTask; });
        else { Section("Liberador de espacio y componentes de Windows"); Line("AVISO", "Omitido", "usa --full para incluirlo (tarda varios minutos)"); }

        return Finish(reportPath, sw);
    }

    private static async Task Step(string title, Func<Task> action)
    {
        Section(title);
        var sw = Stopwatch.StartNew();
        try { await action(); }
        catch (Exception ex) { Line("FALLO", "Excepción", ex.ToString()); }
        Report.AppendLine($"        ({sw.Elapsed.TotalSeconds:N1} s)");
    }

    private static int Finish(string path, Stopwatch sw)
    {
        Report.AppendLine().AppendLine($"RESULTADO: {_ok} correctas, {_fail} fallos, {_warn} avisos · {sw.Elapsed.TotalSeconds:N0} s");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, Report.ToString(), Encoding.UTF8);
        }
        catch { }
        return _fail;
    }

    // ---------------- 1. Instalar un programa falso, desinstalarlo y borrar los restos ----------------
    private static string InstallDir => Path.Combine(Util.Env("ProgramFiles"), AppName);

    private static void FakeInstall()
    {
        Directory.CreateDirectory(InstallDir);
        var exe = Path.Combine(InstallDir, "prueba.exe");
        File.Copy(Path.Combine(Util.WinDir, @"System32\timeout.exe"), exe, true);
        var uninstaller = Path.Combine(InstallDir, "desinstalar.cmd");
        // Como muchos desinstaladores reales: quita su entrada pero deja restos
        File.WriteAllText(uninstaller, $"@echo off\r\nreg delete \"HKLM\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\{KeyName}\" /f /reg:64\r\n");

        using (var k = Registry.LocalMachine.CreateSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{KeyName}"))
        {
            k.SetValue("DisplayName", AppName);
            k.SetValue("Publisher", "OpenSweep Tests");
            k.SetValue("DisplayVersion", "1.0");
            k.SetValue("InstallLocation", InstallDir);
            k.SetValue("DisplayIcon", exe);
            k.SetValue("UninstallString", $"\"{uninstaller}\"");
            k.SetValue("QuietUninstallString", $"\"{uninstaller}\"");
        }
        Directory.CreateDirectory(Path.Combine(Util.RoamingAppData, AppName));
        File.WriteAllText(Path.Combine(Util.RoamingAppData, AppName, "config.ini"), "[prueba]");
        Directory.CreateDirectory(Path.Combine(Util.LocalAppData, AppName, "cache"));
        File.WriteAllText(Path.Combine(Util.LocalAppData, AppName, "cache", "datos.tmp"), new string('x', 100_000));
        using (var k = Registry.CurrentUser.CreateSubKey($@"Software\{AppName}")) k.SetValue("Idioma", "es");

        // Restos profundos: servicio, tarea, firewall, menú contextual y asociación de archivos
        Util.Run("sc.exe", $"create {Service} binPath= \"\\\"{exe}\\\"\" start= demand DisplayName= \"{AppName}\"", 30_000);
        Util.Run("schtasks.exe", $"/Create /TN {KeyName} /TR \"\\\"{exe}\\\" 1\" /SC ONLOGON /F", 30_000);
        Util.Run("netsh.exe", $"advfirewall firewall add rule name=\"{FwRule}\" dir=in action=allow program=\"{exe}\"", 30_000);
        using (var k = Registry.LocalMachine.CreateSubKey($@"SOFTWARE\Classes\*\shell\{KeyName}\command")) k.SetValue("", $"\"{exe}\" \"%1\"");
        using (var k = Registry.LocalMachine.CreateSubKey($@"SOFTWARE\Classes\{KeyName}.Doc\shell\open\command")) k.SetValue("", $"\"{exe}\" \"%1\"");
        using (var k = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Classes\.osprueba")) k.SetValue("", $"{KeyName}.Doc");
    }

    private static bool ServiceExists() => Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{Service}") is { } k && k.GetValue("DeleteFlag") is not 1;
    private static bool TaskExists() => Util.Run("schtasks.exe", $"/Query /TN {KeyName}", 30_000).exitCode == 0;
    private static bool FirewallExists() => Util.Run("netsh.exe", $"advfirewall firewall show rule name=\"{FwRule}\"", 30_000).exitCode == 0;
    private static bool KeyExists(RegistryKey root, string path) { using var k = root.OpenSubKey(path); return k != null; }

    private static async Task UninstallTest()
    {
        var progress = new Progress<string>();
        var before = InstallMonitor.Take(progress);
        FakeInstall();
        var after = InstallMonitor.Take(progress);
        var trace = InstallMonitor.Diff(before, after, "prueba-setup.exe");
        Check(trace.Items.Any(i => i.Kind == "dir" && i.Path.Equals(InstallDir, StringComparison.OrdinalIgnoreCase)), "Monitor de instalaciones", $"detecta la carpeta del programa ({trace.Items.Count} elementos nuevos)");
        Check(trace.UninstallKey?.EndsWith(KeyName) == true, "Monitor de instalaciones", $"detecta la entrada de desinstalación ({trace.Name})");
        Check(ServiceExists() && TaskExists() && FirewallExists(), "Preparación", "servicio, tarea y regla del firewall creados");

        var p = UninstallerService.GetInstalledPrograms().FirstOrDefault(x => x.KeyName == KeyName);
        Check(p != null, "Lista de programas", p != null ? $"aparece «{p.Name}» de {p.Publisher}" : "no aparece el programa de prueba");
        if (p == null) return;

        var code = await UninstallerService.RunUninstallerAsync(p, quiet: true);
        Check(!UninstallerService.StillInstalled(p), "Desinstalación silenciosa", $"código {code}, entrada eliminada");

        var leftovers = UninstallerService.FindLeftovers(p, ScanMode.Moderado, CancellationToken.None);
        foreach (var l in leftovers) Report.AppendLine($"          {(l.Selected ? "[x]" : "[ ]")} {l.Kind,-22} {l.Display}  · {l.ReasonText}");
        bool Has(Func<Leftover, bool> f) => leftovers.Any(l => l.Selected && f(l));
        Check(Has(l => l.Special == "service" && l.Target == Service), "Restos", "servicio encontrado y marcado");
        Check(Has(l => l.Special == "task"), "Restos", "tarea programada encontrada y marcada");
        Check(Has(l => l.Special == "firewall"), "Restos", "regla del firewall encontrada y marcada");
        Check(Has(l => l.IsRegistry && l.Path.Contains(@"*\shell\" + KeyName, StringComparison.OrdinalIgnoreCase)), "Restos", "menú contextual encontrado y marcado");
        Check(Has(l => l.IsRegistry && l.Path.Contains(KeyName + ".Doc", StringComparison.OrdinalIgnoreCase)), "Restos", "asociación de archivos encontrada y marcada");
        Check(Has(l => !l.IsRegistry && l.Path.Equals(InstallDir, StringComparison.OrdinalIgnoreCase)), "Restos", "carpeta de Program Files");
        Check(Has(l => !l.IsRegistry && l.Path.StartsWith(Util.RoamingAppData, StringComparison.OrdinalIgnoreCase)), "Restos", "carpeta de AppData");
        Check(Has(l => l.IsRegistry && l.Path.Equals($@"Software\{AppName}", StringComparison.OrdinalIgnoreCase)), "Restos", "clave del registro del usuario");

        // Nada ajeno marcado: todo lo seleccionado tiene que ser del programa de prueba
        var selected = leftovers.Where(l => l.Selected).ToList();
        var foreign = selected.Where(l => !(l.Display.Contains(AppName, StringComparison.OrdinalIgnoreCase) || l.Display.Contains(KeyName, StringComparison.OrdinalIgnoreCase)
                                            || (l.Target ?? "").Contains(KeyName, StringComparison.OrdinalIgnoreCase))).ToList();
        Check(foreign.Count == 0, "Seguridad", foreign.Count == 0 ? "no se marcó nada ajeno al programa" : "marcados ajenos: " + string.Join(" | ", foreign.Select(f => f.Display)));

        UninstallerService.BackupRegistryLeftovers(selected);
        var (ok, failed) = UninstallerService.DeleteLeftovers(selected.Except(foreign));
        Check(failed == 0, "Borrado de restos", $"{ok} eliminados, {failed} fallos");
        Check(!ServiceExists(), "Comprobación", "el servicio ya no existe");
        Check(!TaskExists(), "Comprobación", "la tarea ya no existe");
        Check(!FirewallExists(), "Comprobación", "la regla del firewall ya no existe");
        Check(!Directory.Exists(InstallDir) && !Directory.Exists(Path.Combine(Util.RoamingAppData, AppName)), "Comprobación", "carpetas eliminadas");
        Check(!KeyExists(Registry.CurrentUser, $@"Software\{AppName}") && !KeyExists(Registry.LocalMachine, $@"SOFTWARE\Classes\*\shell\{KeyName}"), "Comprobación", "claves eliminadas");
        Check(Util.SafeFiles(AppSettings.BackupFolder, "*", true).Any(), "Copia de seguridad", $"creada en {AppSettings.BackupFolder}");
    }

    // ---------------- 2. Limpieza con las reglas por defecto ----------------
    private static void CleanTest()
    {
        var temp = Path.GetTempPath();
        var junk = Path.Combine(temp, "opensweep-prueba.tmp");
        File.WriteAllText(junk, new string('x', 500_000));
        File.SetLastWriteTime(junk, DateTime.Now.AddDays(-10));

        var rules = JunkCleaner.BuildRules().Where(r => r.DefaultOn).ToList();
        long found = 0, cleaned = 0, failed = 0;
        foreach (var r in rules)
        {
            found += JunkCleaner.Analyze(r, false, CancellationToken.None).Bytes;
            var res = JunkCleaner.Clean(r, CancellationToken.None);
            cleaned += res.Bytes; failed += res.Failed;
        }
        Check(rules.Count > 0, "Reglas", $"{rules.Count} reglas activas por defecto");
        Check(cleaned > 0, "Limpieza", $"encontrado {Util.FormatBytes(found)}, liberado {Util.FormatBytes(cleaned)}, {failed} archivos en uso");
        Check(!File.Exists(junk), "Comprobación", "el archivo temporal de prueba se borró");
        Check(Directory.Exists(Util.UserProfile) && Directory.Exists(Path.Combine(Util.WinDir, "System32")), "Seguridad", "las carpetas del usuario y del sistema siguen ahí");
    }

    // ---------------- 3. Registro ----------------
    private static void RegistryTest()
    {
        const string fake = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\opensweep-prueba.exe";
        using (var k = Registry.LocalMachine.CreateSubKey(fake)) k.SetValue("", @"C:\NoExiste\opensweep-prueba.exe");

        var issues = RegistryCleaner.Scan(RegistryCleaner.Categories().Where(c => c.DefaultOn).Select(c => c.Name), null, CancellationToken.None);
        Check(issues.Any(i => i.KeyPath.EndsWith("opensweep-prueba.exe", StringComparison.OrdinalIgnoreCase)), "Análisis", $"{issues.Count} problemas, incluido el de prueba");
        var backup = RegistryCleaner.Backup(issues);
        Check(backup != null && File.Exists(backup), "Copia de seguridad", backup ?? "no creada");
        var (fixedCount, failed) = RegistryCleaner.Fix(issues);
        Check(failed == 0, "Reparación", $"{fixedCount} reparados, {failed} fallos");
        Check(!KeyExists(Registry.LocalMachine, fake), "Comprobación", "la entrada rota de prueba ya no existe");
        if (backup != null)
        {
            var (code, _) = Util.Run("reg.exe", $"import \"{backup}\"", 60_000);
            Check(code == 0 && KeyExists(Registry.LocalMachine, fake), "Restauración", "la copia .reg se importa y devuelve la entrada");
            Registry.LocalMachine.DeleteSubKeyTree(fake, false);
        }
    }

    // ---------------- 4. Privacidad ----------------
    private static void PrivacyTest()
    {
        var all = PrivacyService.Load();
        int already = all.Count(s => s.Enabled);
        int ok = all.Count(s => s.Enabled || PrivacyService.Set(s, true));
        Check(ok == all.Count && PrivacyService.Load().All(s => s.Enabled), "Aplicar", $"{ok} de {all.Count} ajustes aplicados");
        foreach (var s in PrivacyService.Load()) PrivacyService.Set(s, false);
        var after = PrivacyService.Load();
        Check(after.Count(s => s.Enabled) == 0 || already > 0, "Restaurar", $"{after.Count(s => s.Enabled)} siguen activos después de restaurar");
    }

    // ---------------- 5. Tareas programadas ----------------
    private static bool OsTask(string name) =>
        Util.PowerShell($"if (Get-ScheduledTask -TaskPath '\\OpenSweep\\' -TaskName '{name}' -ErrorAction SilentlyContinue) {{ exit 0 }} else {{ exit 1 }}").exitCode == 0;

    private static void SchedulerTest()
    {
        Check(Scheduler.SetAutoClean("weekly") && OsTask("Limpieza automatica"), "Limpieza automática", "tarea semanal creada");
        Check(Scheduler.SetTray(true) && OsTask("Monitor"), "Monitor de la bandeja", "tarea al iniciar sesión creada");
        var freed = Scheduler.RunAutoClean();
        Check(freed >= 0, "Ejecución desatendida", $"liberado {Util.FormatBytes(freed)}");
        Scheduler.SetAutoClean("never");
        Scheduler.SetTray(false);
        Check(!OsTask("Limpieza automatica") && !OsTask("Monitor"), "Desactivar", "tareas eliminadas");
    }

    // ---------------- 6. winapp2 ----------------
    private static async Task Winapp2Test()
    {
        var (ok, message) = await Winapp2.DownloadAsync();
        if (!ok) { Line("AVISO", "Descarga", $"sin conexión: {message}"); return; }
        var entries = Winapp2.Parse();
        var rules = Winapp2.BuildRules();
        Check(entries.Count > 1000, "Lectura", $"{entries.Count} entradas, {rules.Count} programas detectados en este sistema");
        Winapp2.Remove();
    }

    // ---------------- 7. Opcional: cleanmgr y DISM ----------------
    private static void SystemTest()
    {
        Check(SystemCleanup.RunCleanmgr("Temporary Files", "Thumbnail Cache"), "Liberador de espacio", "cleanmgr /sagerun terminó");
        Check(SystemCleanup.ComponentCleanup(), "DISM", "StartComponentCleanup terminó");
    }
}
