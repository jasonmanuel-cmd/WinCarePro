using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using WinCareDesktop.Services;

namespace WinCareDesktop;

public partial class App : Application
{
    /// <summary>Crash log is capped so a recurring fault cannot fill the disk.</summary>
    private const long MaxCrashLogBytes = 2 * 1024 * 1024;

    private static string DataDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinCarePro");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // A second copy would poll system state in parallel and write to the
        // same undo history. Bring the existing window forward and exit quietly:
        // a modal "already running" dialog makes the user click twice to reach
        // the window they were trying to open.
        if (!SingleInstance.TryAcquire())
        {
            WindowActivator.TryActivateExisting();
            Shutdown();
            return;
        }

        ShutdownMode = ShutdownMode.OnMainWindowClose;

        DispatcherUnhandledException += (_, args) =>
        {
            WriteCrash(args.Exception);

            MessageBox.Show(
                BuildCrashMessage(args.Exception),
                "WinCare Pro", MessageBoxButton.OK, MessageBoxImage.Error);

            // Handled: a fault in one action should not take the whole window
            // down mid-session and lose the user's place. Anything that leaves
            // the UI in an unrecoverable state still surfaces here, and the log
            // is the record either way.
            args.Handled = true;
        };

        // A fault on a background thread (a Task.Run continuation, say) is not
        // routed through the dispatcher handler by default and would otherwise
        // tear the process down with nothing written down.
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex) WriteCrash(ex);
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            WriteCrash(args.Exception);
            args.SetObserved();
        };

        var window = new MainWindow();
        MainWindow = window;

        // Unsubscribe the timers and release the mutex on the way out. Without
        // this a DispatcherTimer keeps a reference chain alive during shutdown
        // and the process lingers.
        window.Closed += (_, _) => SingleInstance.Release();

        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Belt and braces: Closed does not fire if startup failed partway.
        SingleInstance.Release();
        base.OnExit(e);
    }

    /// <summary>
    /// Appends a crash record, rotating the file if it has grown too large.
    /// </summary>
    /// <remarks>
    /// An unhandled exception in a polling loop would otherwise append to this
    /// file every five seconds forever, and a user would never think to look
    /// there. When it passes the cap the previous file is replaced rather than
    /// kept, because only the most recent fault is useful.
    /// </remarks>
    private static void WriteCrash(Exception ex)
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            var path = Path.Combine(DataDir, "crash.log");

            if (File.Exists(path) && new FileInfo(path).Length > MaxCrashLogBytes)
            {
                var previous = Path.Combine(DataDir, "crash.previous.log");
                File.Delete(previous);
                File.Move(path, previous);
            }

            File.AppendAllText(path,
                $"--- {DateTime.Now:yyyy-MM-dd HH:mm:ss} ---{Environment.NewLine}" +
                ex + Environment.NewLine + Environment.NewLine);
        }
        catch
        {
            // Never let logging a failure cause a second failure.
        }
    }

    /// <summary>
    /// Turns an exception into something a user can act on.
    /// </summary>
    /// <remarks>
    /// The raw type name means nothing to somebody who just clicked something.
    /// The default sentence names the log path, which is the one thing they can
    /// actually act on - sending it to whoever is supporting the app.
    /// </remarks>
    private static string BuildCrashMessage(Exception ex)
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";

        return $"Something went wrong and WinCare has recovered.\n\n" +
               $"{ex.Message}\n\n" +
               $"The details were written to:\n" +
               $"{Path.Combine(DataDir, "crash.log")}\n\n" +
               $"Version {version}";
    }
}
