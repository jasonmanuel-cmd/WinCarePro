using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using WinCareDesktop.ViewModels;
using WinCareDesktop.Views;
using Xunit;

namespace WinCareDesktop.Tests;
public class GuidanceUiTests
{
    [Fact]
    public void Guidance_navigates_to_existing_panel_without_applying_preferences()
    {
        UiTestHost.Run(() =>
        {
            var main = new MainViewModel();
            main.DashboardVM.OpenGuidanceAsync(new("privacy", "Fixture", "Fixture", "Privacy Shield", "Review")).GetAwaiter().GetResult();
            Assert.Same(main.PrivacyVM, main.CurrentView);
            Assert.True(main.NavigationItems.Single(n => n.Title == "Privacy Shield").IsSelected);
            main.SchedulerVM.Dispose();
        });
    }

    [Fact]
    public void Missing_live_capacity_is_unknown_not_zero_usage_or_a_health_score()
    {
        UiTestHost.Run(() =>
        {
            var main = new MainViewModel { TotalRamGb = 0, TotalStorageGb = 0 };
            Assert.Equal("Unknown", main.RamUsageText);
            Assert.Equal("Unknown", main.StorageUsageText);
            Assert.Equal("—", main.ScoreDisplay);
            main.TotalRamGb = 16; main.TotalStorageGb = 500; main.RamPercent = 40; main.StoragePercent = 70;
            Assert.Equal("40%", main.RamUsageText); Assert.Equal("70%", main.StorageUsageText);
            main.SchedulerVM.Dispose();
        });
    }

    [Fact]
    public void Dashboard_has_named_keyboard_actions_and_readonly_preview()
    {
        UiTestHost.Run(() =>
        {
            var main = new MainViewModel();
            var view = new DashboardView { DataContext = main.DashboardVM };
            var window = new Window { Content = view, Width = 900, Height = 720 };
            window.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/WinCare Pro;component/Themes/Theme.xaml") });
            window.Show(); window.UpdateLayout();
            try
            {
                foreach (var name in new[] { "RescanGuidanceButton", "CancelGuidanceButton", "PreviewExportButton", "SaveExportButton" })
                {
                    var button = Assert.IsType<Button>(view.FindName(name));
                    Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(button)));
                    Assert.True(button.Focusable);
                    Assert.NotNull(button.FocusVisualStyle);
                }
                Assert.True(Assert.IsType<TextBox>(view.FindName("ExportPreviewBox")).IsReadOnly);
            }
            finally { window.Close(); main.SchedulerVM.Dispose(); }
        });
    }
}
