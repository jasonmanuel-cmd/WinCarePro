using System;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Xunit;

namespace WinCareDesktop.Tests;

/// <summary>
/// One shared STA thread with a running <see cref="Dispatcher"/> for every WPF
/// test in this assembly.
/// </summary>
/// <remarks>
/// This must be a single long-lived thread, not a thread per test class.
/// <see cref="Application.Current"/> is process-wide, so a second
/// <c>new Application()</c> throws
/// "Cannot create more than one System.Windows.Application instance in the same
/// AppDomain" and takes the whole test host down. Separately, a window whose
/// brushes were created on one thread throws "DependencyObject that belongs to
/// a different thread" when arranged on another, so the dispatcher that owns
/// the Application has to be the one that builds and measures the windows.
/// </remarks>
internal static class UiTestHost
{
    private static readonly Lazy<(Thread Thread, Dispatcher Dispatcher)> Sta = new(() =>
    {
        var ready = new ManualResetEventSlim(false);
        Dispatcher dispatcher = null!;
        var thread = new Thread(() =>
        {
            dispatcher = Dispatcher.CurrentDispatcher;
            if (Application.Current == null)
                new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            ready.Set();
            Dispatcher.Run();
        })
        { IsBackground = true, Name = "wincare-ui-tests" };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(ready.Wait(TimeSpan.FromSeconds(30)), "STA host failed to start");
        return (thread, dispatcher);
    });

    /// <summary>
    /// Runs <paramref name="action"/> on the STA thread and rethrows anything
    /// it threw on this thread, so an xUnit assertion failure is not swallowed
    /// into a bare XunitException from the dispatcher.
    /// </summary>
    public static void Run(Action action)
    {
        var dispatcher = Sta.Value.Dispatcher;
        Exception? failure = null;

        dispatcher.Invoke(() =>
        {
            try { action(); }
            catch (Exception ex) { failure = ex; }
        }, DispatcherPriority.Normal);

        if (failure != null)
            throw failure;
    }

    /// <summary>
    /// Lets queued Dispatcher.Invoke callbacks and the layout pass complete.
    /// Three turns is enough for a Show() plus the queued background probes.
    /// </summary>
    public static void Drain()
    {
        var dispatcher = Sta.Value.Dispatcher;
        for (var i = 0; i < 3; i++)
            dispatcher.Invoke(() => { }, DispatcherPriority.Background);
    }

    /// <summary>
    /// Opens a real window and lets it lay out. Use instead of constructing a
    /// Window alone, because ActualWidth and the visual tree only settle after
    /// a dispatcher turn.
    /// </summary>
    public static TWindow Show<TWindow>(TWindow window) where TWindow : Window
    {
        Run(() => window.Show());
        Drain();
        return window;
    }

    /// <summary>Closes a window without letting a teardown error fail the test.</summary>
    public static void Close(Window window)
    {
        try { Run(() => window.Close()); }
        catch { /* a window that already closed is not a test failure */ }
    }
}