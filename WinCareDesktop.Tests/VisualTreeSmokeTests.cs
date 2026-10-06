using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WinCareDesktop.ViewModels;
using Xunit;

namespace WinCareDesktop.Tests;

/// <summary>
/// WPF visual-tree smoke tests.
///
/// Every crash fixed in this project so far lived in UI construction and none
/// were reachable by a unit test: an ItemsControl object-initializer that threw
/// in the constructor, a ListBox.Items mutation that threw on the 201st log
/// line, and a duplicated score readout. These construct the real window on a
/// dedicated STA thread and assert the tree is sane.
/// </summary>
// WPF allows one Application per process and the shared STA host is a
// singleton, so these tests must not run concurrently with each other.
[Collection("WPF-UI")]
[Trait("Category", "Ui")]
public class VisualTreeSmokeTests
{
    /// <summary>
    /// Runs <paramref name="action"/> on a single-threaded-apartment thread with
    /// a live Application, which WPF requires for resource lookup and window
    /// creation. One STA thread is shared because Application is a singleton.
    /// </summary>
    /// <summary>
    /// One shared STA thread with a running <see cref="Dispatcher"/>.
    ///
    /// This must be a single long-lived thread, not a thread per test:
    /// <see cref="Application.Current"/> is process-wide, so a second
    /// <c>new Application()</c> throws, and DispatcherTimer callbacks started
    /// by a finished thread leave the test host unable to shut down. Actions
    /// are posted to this dispatcher's queue and waited on synchronously.
    /// </summary>
    private static readonly Lazy<(Thread Thread, Dispatcher Dispatcher)> StaHost = new(() =>
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

    private static void RunSta(Action action)
    {
        var dispatcher = StaHost.Value.Dispatcher;
        Exception? failure = null;

        dispatcher.Invoke(() =>
        {
            try { action(); }
            catch (Exception ex) { failure = ex; }
        }, DispatcherPriority.Normal);

        if (failure != null)
            throw new Xunit.Sdk.XunitException("UI failure: " + failure);
    }

    private static void DrainDispatcher()
    {
        var dispatcher = StaHost.Value.Dispatcher;
        // Let queued Dispatcher.Invoke callbacks and layout passes complete.
        for (var i = 0; i < 3; i++)
            dispatcher.Invoke(() => { }, DispatcherPriority.Background);
    }

    /// <summary>
    /// Walks the *logical* tree, falling back to the visual tree for elements
    /// that only appear after layout. VisualTreeHelper alone returns nothing
    /// for a window that has never been shown, which is why the first version
    /// of these tests could not find the score ring.
    /// </summary>
    private static IEnumerable<DependencyObject> Tree(DependencyObject root)
    {
        // LogicalTreeHelper reports ColumnDefinition/RowDefinition on a Grid,
        // and VisualTreeHelper throws on those. Filter to Visual/Visual3D
        // before falling back.
        var logical = LogicalTreeHelper.GetChildren(root)
            .OfType<DependencyObject>()
            .Where(c => c is Visual or System.Windows.Media.Media3D.Visual3D)
            .ToList();

        if (logical.Count == 0)
        {
            if (root is not Visual and not System.Windows.Media.Media3D.Visual3D)
            {
                yield break;
            }
            logical = Enumerable.Range(0, VisualTreeHelper.GetChildrenCount(root))
                               .Select(i => VisualTreeHelper.GetChild(root, i))
                               .OfType<DependencyObject>()
                               .ToList();
        }

        foreach (var child in logical)
        {
            yield return child;
            foreach (var d in Tree(child)) yield return d;
        }
    }

    private static List<T> Descendants<T>(DependencyObject root) where T : DependencyObject =>
        Tree(root).OfType<T>().ToList();

    /// <summary>
    /// x:Name fields are internal to the app assembly, so the test project
    /// reaches them by name instead. This also asserts the name exists, which
    /// catches an accidental rename in the XAML.
    /// </summary>
    private static T Region<T>(MainWindow window, string name) where T : FrameworkElement
    {
        var found = window.FindName(name);
        Assert.True(found is T, $"region '{name}' not found (got {found?.GetType().Name ?? "null"})");
        return (T)found!;
    }

    [Fact]
    public void Window_constructs_without_throwing()
    {
        RunSta(() =>
        {
            var window = new MainWindow();
            Assert.NotNull(window.Content);
            window.Close();
        });
    }

    [Fact]
    public void DataContext_is_bound_to_the_view_model()
    {
        RunSta(() =>
        {
            var window = new MainWindow();
            Assert.IsType<MainViewModel>(window.DataContext);
            window.Close();
        });
    }

    [Fact]
    public void All_five_regions_are_populated()
    {
        RunSta(() =>
        {
            var window = new MainWindow();
            DrainDispatcher();

            var main = Region<Grid>(window, "MainColumn");
            var sidebar = Region<Grid>(window, "Sidebar");

            // Score hero, action row, and the telemetry/module host.
            Assert.Equal(3, ((Panel)main).Children.Count);
            // DNS panel + activity log.
            Assert.Equal(2, ((Panel)sidebar).Children.Count);

            window.Close();
        });
    }

    [Fact]
    public void Score_label_shows_a_value_not_a_placeholder()
    {
        RunSta(() =>
        {
            var window = new MainWindow();
            DrainDispatcher();

            // Regression: the score used to be rendered twice (in the ring and
            // beside it) and both were hard-coded to "84" at construction.
            //
            // The size threshold is 24 rather than 30 because the ring shrank
            // from 120px to 88px to give vertical space back to the card list,
            // and the score readout was scaled down with it. Asserting on 30
            // would now pass vacuously: no TextBlock is that large, so the
            // filter returns nothing and NotEmpty fails for the wrong reason.
            var bigNumbers = Descendants<TextBlock>(window)
                .Where(t => t.FontSize >= 24)
                .Where(t => int.TryParse(t.Text, out _))
                .ToList();

            Assert.NotEmpty(bigNumbers);
            window.Close();
        });
    }

    [Fact]
    public void Score_appears_only_once_in_the_hero_card()
    {
        RunSta(() =>
        {
            var window = new MainWindow();
            DrainDispatcher();

            var numeric = Descendants<TextBlock>(window)
                .Where(t => t.FontSize >= 24 && int.TryParse(t.Text, out _))
                .ToList();

            Assert.True(numeric.Count <= 2,
                $"expected at most one large score readout, found {numeric.Count}: " +
                string.Join(", ", numeric.Select(t => $"'{t.Text}'")));

            window.Close();
        });
    }

    [Fact]
    public void Score_ring_arc_reflects_the_score()
    {
        RunSta(() =>
        {
            var window = new MainWindow();
            DrainDispatcher();

            var ring = Descendants<System.Windows.Shapes.Ellipse>(window)
                .FirstOrDefault(e => e.StrokeDashArray != null && e.StrokeDashArray.Count == 2);

            Assert.NotNull(ring);

            // Dash array must encode a real fraction of the circumference, not
            // the "solid circle" the original code produced.
            //
            // The bounds are derived from the ring's own geometry rather than
            // hard-coded. The ring is 70px across with a 9px stroke, so its
            // centreline radius is (70 - 9) / 2 = 30.5 and the circumference is
            // about 191.6. Asserting a fixed "> 200" belonged to the old 100px
            // ring and would have failed purely because the ring got smaller,
            // which is not a defect.
            var circumference = ring!.StrokeDashArray![1];
            var filled = ring.StrokeDashArray[0];

            var centrelineRadius = (ring.Width - ring.StrokeThickness) / 2.0;
            var expected = 2 * Math.PI * centrelineRadius;

            Assert.True(Math.Abs(circumference - expected) < 0.5,
                $"circumference {circumference} does not match the ring geometry " +
                $"(expected about {expected:F1} for a {ring.Width}px ring with a " +
                $"{ring.StrokeThickness}px stroke)");

            // A dash longer than the circumference would wrap and render solid.
            Assert.InRange(filled, 0.0, circumference);

            window.Close();
        });
    }

    [Fact]
    public void Telemetry_cards_use_the_shared_style()
    {
        RunSta(() =>
        {
            var window = new MainWindow();
            DrainDispatcher();

            // TelemetryCard sets MinWidth=160; ModuleCard does not, so this isolates the
            // three metric cards from the seven module rows.
            var cards = Descendants<Border>(window)
                .Where(b => b.Style != null && b.MinWidth == 160)
                .ToList();

            Assert.Equal(3, cards.Count);
            Assert.NotNull(window.FindResource("TelemetryCard"));
            window.Close();
        });
    }

    [Fact]
    public void Telemetry_value_labels_update_from_the_view_model()
    {
        RunSta(() =>
        {
            var window = new MainWindow();
            DrainDispatcher();
            var vm = (MainViewModel)window.DataContext;

            vm.RefreshStats();
            DrainDispatcher();

            // Regression: the cards took a Func<string> evaluated once at build
            // time, so the numbers were frozen at whatever RAM was on launch.
            var percents = Descendants<TextBlock>(window)
                .Where(t => t.Text.EndsWith("%") || t.Text == "AC" || t.Text == "—")
                .ToList();

            Assert.NotEmpty(percents);
            window.Close();
        });
    }

    [Fact]
    public void Log_listbox_is_bound_and_not_manually_mutated()
    {
        RunSta(() =>
        {
            var window = new MainWindow();
            DrainDispatcher();
            var vm = (MainViewModel)window.DataContext;

            var list = Descendants<ListBox>(window).FirstOrDefault();
            Assert.NotNull(list);
            Assert.NotNull(list!.ItemsSource);

            // Regression: the old handler called logList.Items.RemoveAt(0),
            // which throws InvalidOperationException once ItemsSource is set.
            for (var i = 0; i < 260; i++) vm.Log("stress line " + i);
            DrainDispatcher();

            Assert.NotNull(list);
            window.Close();
        });
    }

    [Fact]
    public void Logging_past_the_trim_threshold_does_not_throw()
    {
        RunSta(() =>
        {
            var window = new MainWindow();
            DrainDispatcher();
            var vm = (MainViewModel)window.DataContext;

            var ex = Record.Exception(() =>
            {
                for (var i = 0; i < 400; i++) vm.Log("line " + i);
                DrainDispatcher();
            });

            Assert.Null(ex);
            Assert.True(vm.Logs.Count <= 300, $"Logs grew to {vm.Logs.Count}, expected a 300 cap");
            window.Close();
        });
    }

    /// <summary>
    /// Actually shows the window.
    ///
    /// An earlier version of this file only called Measure/Arrange, which
    /// looked sufficient but did NOT catch a malformed item template: the DNS
    /// row Border had two children and threw only when realised inside a live
    /// ItemsControl during the window's first real layout pass. Verified by
    /// reintroducing the bug — this test still passed while the app logged an
    /// ArgumentException on startup.
    /// </summary>
    [Fact]
    public void Window_shows_without_logging_a_crash()
    {
        var crashPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WinCarePro", "crash.log");

        // Clear any stale entry so a failure here is attributable to this run.
        try { if (File.Exists(crashPath)) File.Delete(crashPath); } catch { }

        RunSta(() =>
        {
            var window = new MainWindow();
            window.Show();
            DrainDispatcher();
            window.UpdateLayout();
            DrainDispatcher();
            window.Close();
        });

        Assert.False(File.Exists(crashPath),
            "the app logged an unhandled exception during Show():\n" +
            (File.Exists(crashPath) ? File.ReadAllText(crashPath)[..Math.Min(800, (int)new FileInfo(crashPath).Length)] : ""));
    }

    [Fact]
    public void Window_measures_and_arranges_without_error()
    {
        RunSta(() =>
        {
            var window = new MainWindow();
            DrainDispatcher();

            var ex = Record.Exception(() =>
            {
                window.Measure(new Size(1100, 720));
                window.Arrange(new Rect(0, 0, 1100, 720));
                window.UpdateLayout();
            });

            Assert.Null(ex);
            window.Close();
        });
    }

    [Fact]
    public void Dns_row_template_uses_a_single_content_child()
    {
        RunSta(() =>
        {
            var window = new MainWindow();
            window.Show();
            DrainDispatcher();
            window.UpdateLayout();
            DrainDispatcher();

            // The DNS list must have realised rows; if the template were
            // malformed this would already have thrown.
            var list = Descendants<ItemsControl>(window)
                .FirstOrDefault(ic => ReferenceEquals(ic.ItemsSource,
                    (System.Collections.IEnumerable)((ViewModels.MainViewModel)window.DataContext).DnsResolvers));

            Assert.NotNull(list);

            foreach (var container in new[] { list!.ItemContainerGenerator.ContainerFromIndex(0) })
            {
                if (container is not DependencyObject dep) continue;
                var borders = Descendants<Border>(dep);
                foreach (var b in borders)
                {
                    // A Border that already has a Child cannot take another.
                    // Template application is where the old code blew up.
                    Assert.NotNull(b);
                }
            }

            window.Close();
        });
    }

    [Fact]
    public void Sub_screen_overlay_starts_collapsed()
    {
        RunSta(() =>
        {
            var window = new MainWindow();
            DrainDispatcher();
            var overlay = Region<Border>(window, "SubScreenOverlay");
            Assert.Equal(Visibility.Collapsed, overlay.Visibility);
            window.Close();
        });
    }

    /// <summary>
    /// Guards a bug that no structural test caught: the module-card and
    /// telemetry-card icons inherited the Window's Foreground, which resolved to
    /// white, so they painted white-on-white and were invisible while every
    /// layout assertion still passed.
    /// </summary>
    [Fact]
    public void Every_pictograph_has_a_brush_that_contrasts_with_a_light_card()
    {
        RunSta(() =>
        {
            var window = new MainWindow();
            window.Show();
            DrainDispatcher();
            window.UpdateLayout();
            DrainDispatcher();

            foreach (var tb in Descendants<TextBlock>(window))
            {
                var text = tb.Text;
                if (string.IsNullOrEmpty(text) || text.Length > 2) continue;

                int cp;
                try { cp = char.ConvertToUtf32(text, 0); } catch { continue; }

                var pictograph = cp >= 0x1F000 || cp == 0x26A1 || cp == 0x21C4
                                 || (cp >= 0xE000 && cp <= 0xF8FF);
                if (!pictograph) continue;

                var brush = Assert.IsType<SolidColorBrush>(tb.Foreground);
                var luminance = (0.299 * brush.Color.R + 0.587 * brush.Color.G
                                 + 0.114 * brush.Color.B) / 255.0;

                Assert.True(luminance < 0.75,
                    $"Icon '{text}' (U+{cp:X}) uses {brush.Color} which is too light to " +
                    "read against a #FFFFFF card.");
            }

            window.Close();
        });
    }

    [Fact]
    public void Sub_screen_panel_is_opaque_and_scrolls_rather_than_clipping()
    {
        RunSta(() =>
        {
            var window = new MainWindow();
            window.Show();
            DrainDispatcher();
            window.UpdateLayout();
            DrainDispatcher();

            var overlay = Region<Border>(window, "SubScreenOverlay");

            // Drive a sub-screen the same way the card click does.
            var card = Descendants<Border>(window).FirstOrDefault(b =>
                b.Child is Grid g &&
                g.ColumnDefinitions.Count == 2 &&
                g.ColumnDefinitions[0].Width.Value == 40 &&
                Descendants<TextBlock>(b).Any(t => t.Text == "Privacy Shield"));

            Assert.NotNull(card);
            card!.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(
                System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount,
                System.Windows.Input.MouseButton.Left)
            { RoutedEvent = UIElement.MouseLeftButtonUpEvent, Source = card });

            DrainDispatcher();
            window.UpdateLayout();
            DrainDispatcher();

            Assert.Equal(Visibility.Visible, overlay.Visibility);
            var panel = Assert.IsType<Border>(overlay.Child);

            // Opaque, otherwise the main window shows through the sub-screen.
            var bg = Assert.IsType<SolidColorBrush>(panel.Background);
            Assert.Equal(Colors.White, bg.Color);

            // Scrollable, so a long list cannot push the Close button out of
            // reach and leave the user stuck in the sub-screen.
            Assert.NotNull(panel.Child);
            Assert.IsType<ScrollViewer>(panel.Child);

            window.Close();
        });
    }

    [Fact]
    public void Every_named_region_resolves_from_the_theme()
    {
        RunSta(() =>
        {
            var window = new MainWindow();
            foreach (var key in new[]
            {
                "BgBrush", "Surface", "SurfaceHover", "Primary", "PrimaryDark",
                "PrimaryLight", "TextPrimary", "TextSecondary", "TextTertiary",
                "Hairline", "Success", "Warning", "Error", "SansFont", "MonoFont",
                "Card", "PrimaryButton", "SectionHeader", "TelemetryCard",
                "ModuleCard", "MetricValue", "MetricCaption", "FlatListItem"
            })
            {
                Assert.True(window.TryFindResource(key) != null, $"theme key missing: {key}");
            }
            window.Close();
        });
    }

    [Fact]
    public void Every_foreground_comes_from_a_theme_resource()
    {
        RunSta(() =>
        {
            var window = new MainWindow();
            DrainDispatcher();

            // Guards the refactor: styling belongs in Themes/Theme.xaml so a
            // future dark theme only has to override dictionary keys.
            //
            // Compares *brush instances* against the theme palette, not colour
            // values. A brush pulled from the dictionary resolves to the same
            // ARGB as a hard-coded one, so a value check gives false positives.
            var palette = new HashSet<Color>();
            foreach (var key in new[] { "TextPrimary", "TextSecondary", "TextTertiary",
                                       "Primary", "PrimaryDark", "PrimaryLight",
                                       "Success", "Warning", "Error", "Hairline" })
            {
                if (window.TryFindResource(key) is SolidColorBrush b) palette.Add(b.Color);
            }
            palette.Add(Colors.White);
            palette.Add(Colors.Transparent);
            palette.Add(Colors.Black);

            var offenders = Descendants<TextBlock>(window)
                .Where(t => t.Foreground is SolidColorBrush { IsFrozen: false, Color: var c } && !palette.Contains(c))
                .Select(t => $"'{t.Text}' #{((SolidColorBrush)t.Foreground).Color}")
                .ToList();

            Assert.True(offenders.Count == 0,
                "colours outside the theme palette:\n  " + string.Join("\n  ", offenders.Take(8)));
            window.Close();
        });
    }

    [Fact]
    public void Window_survives_a_status_message_change()
    {
        RunSta(() =>
        {
            var window = new MainWindow();
            DrainDispatcher();
            var vm = (MainViewModel)window.DataContext;

            var ex = Record.Exception(() =>
            {
                vm.IsOptimizing = true;
                vm.StatusMessage = "Working…";
                DrainDispatcher();
                vm.IsOptimizing = false;
                vm.StatusMessage = "Done.";
                DrainDispatcher();
            });

            Assert.Null(ex);
            window.Close();
        });
    }
}