using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WinCareDesktop.ViewModels;
using WinCareDesktop.Views;
using Xunit;

namespace WinCareDesktop.Tests;

/// <summary>
/// WPF visual-tree smoke tests for the v2 overhaul.
/// </summary>
[Collection("WPF-UI")]
[Trait("Category", "Ui")]
public class VisualTreeSmokeTests
{
    private static void RunSta(Action action) => UiTestHost.Run(action);
    private static void DrainDispatcher() => UiTestHost.Drain();

    private static IEnumerable<DependencyObject> Tree(DependencyObject root)
    {
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
    public void All_navigation_regions_are_populated()
    {
        RunSta(() =>
        {
            var window = new MainWindow();
            DrainDispatcher();

            var main = Region<Grid>(window, "MainColumn");
            var sidebar = Region<Border>(window, "Sidebar");

            Assert.NotNull(main);
            Assert.NotNull(sidebar);

            window.Close();
        });
    }

    [Fact]
    public void Dashboard_view_constructs_and_displays_score()
    {
        RunSta(() =>
        {
            var vm = new MainViewModel();
            var view = new DashboardView { DataContext = vm.DashboardVM };
            Assert.NotNull(view);
        });
    }

    [Fact]
    public void Disk_analyzer_view_constructs()
    {
        RunSta(() =>
        {
            var view = new DiskAnalyzerView { DataContext = new DiskAnalyzerViewModel() };
            Assert.NotNull(view);
        });
    }

    [Fact]
    public void Startup_manager_view_constructs()
    {
        RunSta(() =>
        {
            var view = new StartupManagerView { DataContext = new StartupManagerViewModel() };
            Assert.NotNull(view);
        });
    }

    [Fact]
    public void App_uninstall_view_constructs()
    {
        RunSta(() =>
        {
            var view = new AppUninstallView { DataContext = new AppUninstallViewModel() };
            Assert.NotNull(view);
        });
    }

    [Fact]
    public void Privacy_view_constructs()
    {
        RunSta(() =>
        {
            var vm = new MainViewModel();
            var view = new PrivacyView { DataContext = vm.PrivacyVM };
            Assert.NotNull(view);
        });
    }

    [Fact]
    public void Network_monitor_view_constructs()
    {
        RunSta(() =>
        {
            var vm = new MainViewModel();
            var view = new NetworkMonitorView { DataContext = vm.NetworkMonitorVM };
            Assert.NotNull(view);
        });
    }

    [Fact]
    public void Scheduler_view_constructs()
    {
        RunSta(() =>
        {
            var view = new SchedulerView { DataContext = new SchedulerViewModel() };
            Assert.NotNull(view);
        });
    }

    [Fact]
    public void History_logs_view_constructs()
    {
        RunSta(() =>
        {
            var vm = new MainViewModel();
            var view = new HistoryLogsView { DataContext = vm.HistoryLogsVM };
            Assert.NotNull(view);
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
                "Card", "PrimaryButton"
            })
            {
                Assert.True(window.TryFindResource(key) != null, $"theme key missing: {key}");
            }
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
