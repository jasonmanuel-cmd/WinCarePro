using System;
using System.Linq;
using System.Windows;
using WinCareDesktop.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace WinCareDesktop.Tests;

/// <summary>
/// Verifies that v2 view navigation switches smoothly between tool views.
/// </summary>
public class PanelDismissTests
{
    private readonly ITestOutputHelper _o;
    public PanelDismissTests(ITestOutputHelper o) => _o = o;

    private static void RunSta(Action action) => UiTestHost.Run(action);
    private static void Drain() => UiTestHost.Drain();

    [Fact]
    public void Navigation_switches_between_tool_views_cleanly()
    {
        RunSta(() =>
        {
            var window = new MainWindow();
            window.Show();
            Drain();

            var vm = Assert.IsType<MainViewModel>(window.DataContext);
            Assert.IsType<DashboardViewModel>(vm.CurrentView);

            // Navigate to Disk Analyzer
            var diskNav = vm.NavigationItems.First(x => x.Title == "Disk Analyzer");
            vm.NavigateCommand.Execute(diskNav);
            Drain();
            Assert.IsType<DiskAnalyzerViewModel>(vm.CurrentView);

            // Navigate back to Dashboard
            var dashNav = vm.NavigationItems.First(x => x.Title == "Dashboard");
            vm.NavigateCommand.Execute(dashNav);
            Drain();
            Assert.IsType<DashboardViewModel>(vm.CurrentView);

            window.Close();
        });
    }

    [Fact]
    public void All_navigation_items_have_valid_view_models()
    {
        RunSta(() =>
        {
            var vm = new MainViewModel();
            Assert.Equal(8, vm.NavigationItems.Count);

            foreach (var item in vm.NavigationItems)
            {
                Assert.NotNull(item.ViewModel);
                Assert.NotEmpty(item.Title);
                Assert.NotEmpty(item.Icon);
            }
        });
    }

    [Fact]
    public void App_uninstaller_view_model_initializes()
    {
        RunSta(() =>
        {
            var vm = new AppUninstallViewModel();
            Assert.Equal("Interactive App Uninstaller & Bloatware Remover", vm.Title);
            Assert.NotNull(vm.DisplayedApps);
        });
    }

    [Fact]
    public void Scheduler_view_model_loads_and_saves()
    {
        RunSta(() =>
        {
            var vm = new SchedulerViewModel();
            Assert.NotNull(vm.Config);
            vm.ToggleEnabledCommand.Execute(null);
            Assert.NotNull(vm.StatusMessage);
        });
    }
}
