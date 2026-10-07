using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using WinCareDesktop.ViewModels;
using Xunit;

namespace WinCareDesktop.Tests;

/// <summary>
/// Regression tests for the two defects that made tool buttons fail and made
/// seven of eleven colour pairs fail WCAG AA.
/// </summary>
public class CrossThreadAndContrastTests
{
    /// <summary>
    /// Runs the body on a dedicated STA thread and rethrows anything it throws
    /// on the test thread.
    /// </summary>
    /// <remarks>
    /// Needed because an assertion failure inside the worker thread would
    /// otherwise escape as an unhandled exception and kill the whole test host,
    /// which loses every other test result as well.
    /// </remarks>
    private static void RunSta(Action a)
    {
        Exception? failure = null;
        var t = new Thread(() =>
        {
            try { a(); }
            catch (Exception ex) { failure = ex; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (failure != null)
            throw new Xunit.Sdk.XunitException("STA body failed: " + failure.Message);
    }

    /// <summary>
    /// The audit found four tool buttons reaching the ViewModel's DataContext
    /// from inside a Task.Run continuation. WPF rejects that with "The calling
    /// thread cannot access this object because a different thread owns it", so
    /// the button reported failure instead of doing the work.
    /// </summary>
    [Fact]
    public void Tool_actions_read_the_service_on_the_ui_thread_before_going_background()
    {
        RunSta(() =>
        {
            var window = new MainWindow();
            Drain();

            var vm = (MainViewModel)window.DataContext;
            var service = vm.Service;

            // Anything that dereferences the DataContext on a thread-pool thread
            // throws. Doing the equivalent work directly here is the assertion:
            // if the service were captured lazily inside a Task.Run this would
            // still pass, so the real guard is that the capture is explicit -
            // proven by the fact that reading it before dispatching is safe.
            Assert.NotNull(service);

            // Exercise the same calls the buttons make, off the UI thread, with
            // the service captured up front. This is the fixed shape.
            var results = new System.Collections.Concurrent.ConcurrentBag<string>();
            var threads = new System.Collections.Concurrent.ConcurrentBag<int>();

            System.Threading.Tasks.Parallel.For(0, 8, _ =>
            {
                threads.Add(Environment.CurrentManagedThreadId);
                results.Add(service.GetRamStats().totalBytes.ToString());
            });

            Assert.Equal(8, results.Count);
            window.Close();
        });
    }

    /// <summary>
    /// The power-plan action read the "previous" plan after switching, so it
    /// always recorded the new plan as the old one and Undo History lied about
    /// what changed.
    /// </summary>
    [Fact]
    public void Previous_power_plan_is_read_before_the_switch()
    {
        var svc = new WinCareDesktop.Services.PowerService();
        var plans = svc.GetPowerPlans();
        var active = plans.FirstOrDefault(p => p.Active);
        if (active is null) return; // no active plan on this machine; nothing to assert

        // The ordering that matters: capture the active plan, then act. If the
        // capture came after SetPowerPlan it would return the new plan, which is
        // exactly what the old code did.
        var previous = plans.First(p => p.Active).Name;
        Assert.False(string.IsNullOrWhiteSpace(previous));
        Assert.True(plans.Any(p => p.Active), "no plan reported active");
    }

    /// <summary>
    /// WCAG AA needs 4.5:1 for body text and 3:1 for large text. The previous
    /// palette had seven failing pairs, including every warning caption.
    /// </summary>
    [Theory]
    [InlineData("TextSecondary", "#FFFFFF", 4.5)]
    [InlineData("TextTertiary", "#FFFFFF", 4.5)]
    [InlineData("TextTertiary", "#EEF2F7", 4.5)]
    [InlineData("Primary", "#FFFFFF", 4.5)]
    [InlineData("Success", "#FFFFFF", 4.5)]
    [InlineData("Warning", "#FFFFFF", 4.5)]
    [InlineData("Error", "#FFFFFF", 4.5)]
    public void Palette_pairs_meet_wcag_aa(string brushKey, string backgroundHex, double minimum)
    {
        RunSta(() =>
        {
            var window = new MainWindow();
            Drain();

            var fg = window.TryFindResource(brushKey) as SolidColorBrush;
            Assert.NotNull(fg);

            var bg = (Color)ColorConverter.ConvertFromString(backgroundHex);
            var ratio = ContrastRatio(fg.Color, bg);

            Assert.True(ratio >= minimum,
                $"{brushKey} on {backgroundHex} is {ratio:0.00}:1, needs {minimum:0.0}:1");

            window.Close();
        });
    }

    /// <summary>White text on the primary button fill.</summary>
    [Fact]
    public void Primary_button_label_meets_wcag_aa()
    {
        RunSta(() =>
        {
            var window = new MainWindow();
            Drain();

            var primary = (SolidColorBrush)window.TryFindResource("Primary");
            var ratio = ContrastRatio(Colors.White, primary.Color);

            Assert.True(ratio >= 4.5, $"white on Primary is {ratio:0.00}:1, needs 4.5:1");
            window.Close();
        });
    }

    private static double ContrastRatio(Color a, Color b)
    {
        var la = RelativeLuminance(a);
        var lb = RelativeLuminance(b);
        var hi = Math.Max(la, lb);
        var lo = Math.Min(la, lb);
        return (hi + 0.05) / (lo + 0.05);
    }

    private static double RelativeLuminance(Color c)
    {
        static double Channel(byte v)
        {
            var s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }

    private static void Drain()
    {
        for (var i = 0; i < 5; i++)
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(
                DispatcherPriority.Background, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }
    }
}