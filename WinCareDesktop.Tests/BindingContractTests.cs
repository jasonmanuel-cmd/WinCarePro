using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WinCareDesktop.ViewModels;
using WinCareDesktop.Views;
using Xunit;
namespace WinCareDesktop.Tests;
public class BindingContractTests
{
    [Fact]
    public void Uninstall_action_is_labeled_and_long_status_wraps()
    {
        UiTestHost.Run(() =>
        {
            var vm = new AppUninstallViewModel { StatusText = "Partial inventory: Store enumeration failed; desktop rows remain available." };
            var view = new AppUninstallView { DataContext = vm };
            var window = new Window { Content = view, Width = 900, Height = 700 };
            window.Show(); window.UpdateLayout();
            try
            {
                var action = Walk(view).OfType<Button>().Single(b => b.Command == vm.UninstallSelectedCommand);
                Assert.Contains("Uninstall", string.Join(" ", Walk(action).OfType<TextBlock>().Select(t => t.Text)));
                var status = Walk(view).OfType<TextBlock>().Single(t => t.GetBindingExpression(TextBlock.TextProperty)?.ParentBinding.Path?.Path == "StatusText");
                Assert.Equal(TextWrapping.Wrap, status.TextWrapping);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void Privacy_and_history_render_status_feedback()
    {
        UiTestHost.Run(() =>
        {
            var vm = new MainViewModel();
            foreach (var view in new UserControl[] { new PrivacyView { DataContext = vm.PrivacyVM }, new HistoryLogsView { DataContext = vm.HistoryLogsVM } })
            {
                var window = new Window { Content = view }; window.Show(); window.UpdateLayout();
                try { Assert.Contains(Walk(view).OfType<TextBlock>(), t => t.GetBindingExpression(TextBlock.TextProperty)?.ParentBinding.Path?.Path == "StatusText"); }
                finally { window.Close(); }
            }
        });
    }
    [Fact]
    public void Adapter_selection_carries_stable_identity_to_dns_service()
    {
        UiTestHost.Run(() =>
        {
            var vm = new MainViewModel();
            vm.NetworkMonitorVM.SelectedAdapter = new WinCareDesktop.Services.NetworkInterfaceInfo { Id = "fixture-id", Name = "Duplicate name" };
            Assert.Equal("fixture-id", vm.Service.SelectedDnsAdapterId);
        });
    }
    private static IEnumerable<DependencyObject> Walk(DependencyObject root)
    {
        yield return root;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Walk(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
}
