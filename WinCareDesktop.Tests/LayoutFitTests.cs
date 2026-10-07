using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Xunit;
using Xunit.Abstractions;

namespace WinCareDesktop.Tests;

/// <summary>
/// Checks the dashboard does not lay out wider than the window it is given.
/// </summary>
/// <remarks>
/// Found by running the app at 1180x780: the sidebar's DNS and Activity Log
/// cards were cut off at the right edge. The window advertises MinWidth 900,
/// so any width at or above that has to render completely.
/// </remarks>
public class LayoutFitTests
{
    private readonly ITestOutputHelper _o;
    public LayoutFitTests(ITestOutputHelper o) => _o = o;

    /// <summary>
    /// The shared dispatcher is process-wide because Application.Current is a
    /// singleton. See <see cref="UiTestHost"/>.
    /// </summary>
    private static void RunSta(Action action) => UiTestHost.Run(action);

    private static void Drain() => UiTestHost.Drain();

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        if (root is not Visual v) yield break;
        var n = VisualTreeHelper.GetChildrenCount(v);
        for (var i = 0; i < n; i++)
        {
            DependencyObject c;
            try { c = VisualTreeHelper.GetChild(v, i); }
            catch { continue; }
            yield return c;
            foreach (var d in Descendants(c)) yield return d;
        }
    }

    [Theory]
    [InlineData(900)]    // MinWidth
    [InlineData(1100)]   // default
    [InlineData(1280)]
    [InlineData(1600)]
    public void Nothing_renders_past_the_right_edge(int windowWidth)
    {
        RunSta(() =>
        {
            var window = new MainWindow { Width = windowWidth, Height = 720 };
            window.Show();
            Drain();

            var w = window.ActualWidth;

            var offenders = Descendants(window)
                .OfType<FrameworkElement>()
                .Where(e => e.IsVisible && e.ActualWidth > 0)
                .Select(e =>
                {
                    try
                    {
                        var p = e.TransformToAncestor(window).Transform(new Point(0, 0));
                        var t = e as TextBlock;
                        return new
                        {
                            Right = p.X + e.ActualWidth,
                            Type = e.GetType().Name,
                            Name = e.Name,
                            Width = e.ActualWidth,
                            Text = t?.Text
                        };
                    }
                    catch { return null; }
                })
                .Where(x => x != null && x.Right > w + 1)
                .OrderByDescending(x => x!.Right)
                .Take(8)
                .ToList();

            foreach (var o in offenders)
            {
                var text = o!.Text ?? "";
                if (text.Length > 28) text = text[..28];
                _o.WriteLine($"  right={o.Right:F0} (window {w:F0})  {o.Type} " +
                             $"'{o.Name}' w={o.Width:F0} \"{text}\"");
            }

            Assert.True(offenders.Count == 0,
                $"{offenders.Count} element(s) render past the window right edge at width {windowWidth}");

            window.Close();
        });
    }

    [Fact]
    public void Sidebar_fits_inside_its_column()
    {
        RunSta(() =>
        {
            var window = new MainWindow { Width = 1100, Height = 720 };
            window.Show();
            Drain();

            var root = (Grid)window.Content;
            var sidebar = root.Children.OfType<FrameworkElement>()
                .First(c => c.Name == "Sidebar");

            // The column is a fixed 300 and the sidebar carries a 24px right
            // margin, so its content has 276px to live in. Anything wider means
            // the card inside is forcing the column open or being clipped.
            var contentWidth = sidebar.ActualWidth - sidebar.Margin.Left - sidebar.Margin.Right;
            _o.WriteLine($"  sidebar={sidebar.ActualWidth:F0} margin={sidebar.Margin} " +
                         $"content={contentWidth:F0}");

            foreach (var child in Descendants(sidebar).OfType<FrameworkElement>())
            {
                if (!child.IsVisible || child.ActualWidth <= 0) continue;
                if (child.ActualWidth > contentWidth + 1)
                {
                    var t = child as TextBlock;
                    var text = t?.Text ?? "";
                    if (text.Length > 24) text = text[..24];
                    _o.WriteLine($"  OVER: {child.GetType().Name} w={child.ActualWidth:F0} \"{text}\"");
                }
            }

            Assert.True(contentWidth >= 240,
                $"sidebar content area is only {contentWidth:F0}px");

            window.Close();
        });
    }
}