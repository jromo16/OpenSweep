using System.Windows;
using OpenSweep.Core;
using OpenSweep.Services;
using OpenSweep.Views;
using Forms = System.Windows.Forms;

namespace OpenSweep;

/// <summary>
/// Monitor en la bandeja del sistema: revisa la basura cada pocas horas y avisa
/// cuando supera el umbral configurado. Desde el menú se puede abrir OpenSweep o limpiar.
/// </summary>
public sealed class TrayHost : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly System.Windows.Threading.DispatcherTimer _timer;
    private bool _busy;

    public TrayHost()
    {
        var iconStream = Application.GetResourceStream(new Uri("pack://application:,,,/app.ico"))?.Stream;
        _icon = new Forms.NotifyIcon
        {
            Icon = iconStream != null ? new System.Drawing.Icon(iconStream) : System.Drawing.SystemIcons.Application,
            Text = "OpenSweep",
            Visible = true,
        };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(Loc.T("Abrir OpenSweep"), null, (_, _) => OpenMain());
        menu.Items.Add(Loc.T("Analizar ahora"), null, async (_, _) => await Check(alwaysShow: true));
        menu.Items.Add(Loc.T("Limpiar ahora"), null, async (_, _) => await Clean());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(Loc.T("Salir del monitor"), null, (_, _) => { Dispose(); Application.Current.Shutdown(); });
        _icon.ContextMenuStrip = menu;
        _icon.DoubleClick += (_, _) => OpenMain();
        _icon.BalloonTipClicked += (_, _) => OpenMain();

        _timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromHours(6) };
        _timer.Tick += async (_, _) => await Check(alwaysShow: false);
        _timer.Start();

        // Limpieza al cerrar el navegador: comprobación ligera cada 5 segundos
        _browserTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _browserTimer.Tick += async (_, _) => await WatchBrowsers();
        _browserTimer.Start();

        // Si se desactiva el monitor en Opciones, se cierra este proceso (no solo la tarea programada)
        _stop = new EventWaitHandle(false, EventResetMode.AutoReset, Scheduler.TrayStopEvent);
        ThreadPool.RegisterWaitForSingleObject(_stop, (_, _) =>
            Application.Current.Dispatcher.BeginInvoke(() => { Dispose(); Application.Current.Shutdown(); }), null, -1, true);

        // Monitor de instalaciones automático: procesos cada 5 s, programas instalados cada 30 s
        _traceTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _traceTimer.Tick += async (_, _) => await WatchInstalls();
        _traceTimer.Start();
        // Primera comprobación unos minutos después de iniciar sesión
        _ = Task.Delay(TimeSpan.FromMinutes(3)).ContinueWith(_ => Application.Current.Dispatcher.Invoke(async () => await Check(false)));
    }

    private static void OpenMain()
    {
        var w = Application.Current.Windows.OfType<MainWindow>().FirstOrDefault() ?? new MainWindow();
        w.Show();
        if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
        w.Activate();
    }

    private async Task Check(bool alwaysShow)
    {
        if (_busy) return;
        _busy = true;
        try
        {
            long junk = await Task.Run(Scheduler.MeasureJunk);
            _icon.Text = $"OpenSweep · {Util.FormatBytes(junk)} {Loc.T("de basura")}";
            if (alwaysShow || junk >= AppSettings.Current.TrayThresholdMb * 1024L * 1024L)
                _icon.ShowBalloonTip(8000, "OpenSweep",
                    $"{Loc.T("Tienes")} {Util.FormatBytes(junk)} {Loc.T("de archivos basura. Haz clic para limpiar.")}", Forms.ToolTipIcon.Info);
        }
        finally { _busy = false; }
    }

    private async Task Clean()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            long freed = await Task.Run(Scheduler.RunAutoClean);
            _icon.ShowBalloonTip(5000, "OpenSweep", $"{Loc.T("Limpieza completada")}: {Util.FormatBytes(freed)} {Loc.T("liberados")}", Forms.ToolTipIcon.Info);
        }
        finally { _busy = false; }
    }

    private readonly System.Windows.Threading.DispatcherTimer _browserTimer;
    private BrowserWatcher? _watcher;
    private bool _watching;

    private async Task WatchBrowsers()
    {
        if (_watching) return;
        _watching = true;
        try
        {
            AppSettings.ReloadIfChanged();
            if (!AppSettings.Current.CleanBrowsersOnClose) { _watcher = null; return; }
            var watcher = _watcher ??= await Task.Run(() => new BrowserWatcher());
            var closed = await Task.Run(watcher.Poll);
            foreach (var browser in closed)
            {
                await Task.Delay(3000);   // el navegador termina de guardar sus archivos al salir
                long freed = await Task.Run(() => BrowserWatcher.Clean(browser));
                if (freed > 1024 * 1024)
                    _icon.ShowBalloonTip(4000, "OpenSweep", $"{browser}: {Util.FormatBytes(freed)} {Loc.T("liberados al cerrarlo")}", Forms.ToolTipIcon.Info);
            }
        }
        catch { }
        finally { _watching = false; }
    }

    private readonly System.Windows.Threading.DispatcherTimer _traceTimer;
    private readonly EventWaitHandle _stop;
    private AutoTrace? _autoTrace;
    private bool _tracing;
    private int _traceTicks;

    private async Task WatchInstalls()
    {
        if (_tracing) return;
        _tracing = true;
        try
        {
            AppSettings.ReloadIfChanged();
            if (!AppSettings.Current.AutoTraceInstalls) { _autoTrace = null; return; }
            if (_autoTrace == null)
            {
                // La primera instantánea tarda alrededor de un minuto: en segundo plano y con prioridad baja
                var t = new AutoTrace();
                await Task.Run(() =>
                {
                    Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;
                    t.TakeBaseline();
                });
                _autoTrace = t;
                return;
            }
            await Task.Run(_autoTrace.PollProcesses);
            if (++_traceTicks % 6 != 0) return;   // cada 30 s
            var trace = await Task.Run(() =>
            {
                Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;
                return _autoTrace.Check();
            });
            if (trace != null)
                _icon.ShowBalloonTip(6000, "OpenSweep",
                    $"{Loc.T("Se registró la instalación de")} {trace.Name}. {Loc.T("Al desinstalarlo, OpenSweep sabrá exactamente qué quitar.")}",
                    Forms.ToolTipIcon.Info);
        }
        catch { }
        finally { _tracing = false; }
    }

    public void Dispose()
    {
        _traceTimer.Stop();
        _browserTimer.Stop();
        _timer.Stop();
        _icon.Visible = false;
        _icon.Dispose();
    }
}
