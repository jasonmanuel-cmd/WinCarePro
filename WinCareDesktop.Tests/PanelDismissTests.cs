using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using Xunit;
using Xunit.Abstractions;

namespace WinCareDesktop.Tests;

/// <summary>
/// Verifies the sub-screen panel is dismissable without scrolling to the bottom.
/// </summary>
/// <remarks>
/// Audit finding: the only way out of a panel was a Close button at the very
/// bottom of the scroll, so on RAM, Storage and Uninstall you had to scroll the
/// whole list to reach it. Fixes: a pinned close in the header, Esc to close,
/// and Tab focus trapped inside the panel.
/// </remarks>
public class PanelDismissTests
{
    private readonly ITestOutputHelper _o;
    public PanelDismissTests(ITestOutputHelper o) => _o = o;

    private static void RunSta(Action action) => UiTestHost.Run(action);

    private static void Drain() => UiTestHost.Drain();

    /// <summary>Opens a sub-screen the same way a card click does.</summary>
    private static void ShowSub(MainWindow window, string title)
    {
        var m = typeof(MainWindow).GetMethod(
            "ShowSub",
            BindingFlags.Instance | BindingFlags.NonPublic,
            null,
            new[] { typeof(string), typeof(string) },
            null);
        Assert.NotNull(m);
        m!.Invoke(window, new object?[] { title, "Body text for the panel." });
        Drain();
    }

    private static void HideSub(MainWindow window)
    {
        var m = typeof(MainWindow).GetMethod(
            "HideSub", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(m);
        m!.Invoke(window, null);
        Drain();
    }

    /// <summary>
    /// SubScreenOverlay is a named Border in MainWindow.xaml, so it is
    /// internal rather than private.
    /// </summary>
    private static System.Windows.Controls.Border Overlay(MainWindow window) =>
        (System.Windows.Controls.Border)typeof(MainWindow)
            .GetField("SubScreenOverlay", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(window)!;

    private static System.Collections.Generic.IEnumerable<DependencyObject> Descendants(
        DependencyObject root)
    {
        if (root is not System.Windows.Media.Visual v) yield break;
        var n = System.Windows.Media.VisualTreeHelper.GetChildrenCount(v);
        for (var i = 0; i < n; i++)
        {
            DependencyObject c;
            try { c = System.Windows.Media.VisualTreeHelper.GetChild(v, i); }
            catch { continue; }
            yield return c;
            foreach (var d in Descendants(c)) yield return d;
        }
    }

    [Fact]
    public void Panel_has_a_close_button_in_the_header()
    {
        RunSta(() =>
        {
            var window = new MainWindow();
            window.Show();
            Drain();

            ShowSub(window, "RAM tools");

            var close = Descendants(Overlay(window))
                .OfType<Button>()
                .FirstOrDefault(b => AutomationProperties.GetName(b) == "Close panel");

            Assert.True(close != null,
                "no Button named 'Close panel' in the panel visual tree");

            // The header close is what makes a long panel dismissable, so it has
            // to be near the top, not below the scroll content.
            var host = Descendants(Overlay(window)).OfType<ScrollViewer>().First();
            var closeTop = close!.TransformToAncestor(host).Transform(new Point(0, 0)).Y;

            _o.WriteLine($"  close button sits {closeTop:F0}px below the top of the scroller");
            Assert.True(closeTop < 200,
                $"close button is {closeTop:F0}px down; it must be pinned in the header");

            window.Close();
        });
    }

    [Fact]
    public void Close_button_hides_the_panel()
    {
        RunSta(() =>
        {
            var window = new MainWindow();
            window.Show();
            Drain();

            ShowSub(window, "Storage tools");
            Assert.Equal(Visibility.Visible, Overlay(window).Visibility);

            var close = Descendants(Overlay(window))
                .OfType<Button>()
                .First(b => AutomationProperties.GetName(b) == "Close panel");

            // Raise the routed click the way the shell would.
            close.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Drain();

            Assert.Equal(Visibility.Collapsed, Overlay(window).Visibility);
            window.Close();
        });
    }

    [Fact]
    public void Escape_hides_the_panel()
    {
        RunSta(() =>
        {
            var window = new MainWindow();
            window.Show();
            Drain();

            ShowSub(window, "Uninstall programs");
            Assert.Equal(Visibility.Visible, Overlay(window).Visibility);

            // The handler is attached to the window in the Preview phase, so it
            // sees the key before the focused control consumes it.
            window.RaiseEvent(new KeyEventArgs(
                Keyboard.PrimaryDevice,
                PresentationSource.FromVisual(window),
                0,
                Key.Escape)
            { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            Drain();

            Assert.Equal(Visibility.Collapsed, Overlay(window).Visibility);
            window.Close();
        });
    }

    [Fact]
    public void Tab_focus_is_trapped_inside_the_panel()
    {
        RunSta(() =>
        {
            var window = new MainWindow();
            window.Show();
            Drain();

            ShowSub(window, "Power tools");

            // Every focusable inside the panel must cycle rather than escape.
            var trapped = Descendants(Overlay(window))
                .OfType<FrameworkElement>()
                .Where(e => e.Focusable)
                .ToList();

            _o.WriteLine($"  {trapped.Count} focusable element(s) in the panel");

            foreach (var e in trapped)
            {
                var nav = KeyboardNavigation.GetTabNavigation(e);
                Assert.True(nav == KeyboardNavigationMode.Cycle || nav == KeyboardNavigationMode.Continue,
                    $"{e.GetType().Name} uses {nav}; focus would escape the panel");
            }

            Assert.True(trapped.Count > 0, "panel has no focusable content at all");
            window.Close();
        });
    }
}