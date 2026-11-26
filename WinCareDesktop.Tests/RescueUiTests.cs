using WinCareDesktop.ViewModels;
using Xunit;
namespace WinCareDesktop.Tests;
public class RescueUiTests
{
    [Fact] public void Window_polling_uses_applied_low_resource_interval()
    {
        UiTestHost.Run(()=>{
            var window=new WinCareDesktop.MainWindow(); window.Show();
            try {
                var timer=(System.Windows.Threading.DispatcherTimer)typeof(WinCareDesktop.MainWindow).GetField("_pollTimer",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.GetValue(window)!;
                Assert.Equal(TimeSpan.FromSeconds(15),timer.Interval);
            } finally {window.Close();}
        });
    }
    [Fact] public void Diagnostic_file_policy_is_off_in_safe_mode()
    {
        var policy=typeof(WinCareDesktop.App).GetProperty("ShouldWriteDiagnostics",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(policy); Assert.Equal(false,policy.GetValue(null));
    }
    [Fact] public void Rescue_pages_are_reachable_without_running_actions()
    {
        UiTestHost.Run(()=>{
            var vm=new MainViewModel();
            try {
                foreach(var title in new[]{"Memory & Apps","Defender","Options"})
                {
                    var item=Assert.Single(vm.NavigationItems,n=>n.Title==title);
                    vm.Navigate(item); Assert.Same(item.ViewModel,vm.CurrentView);
                }
            } finally { vm.SchedulerVM.Dispose(); }
        });
    }
}
