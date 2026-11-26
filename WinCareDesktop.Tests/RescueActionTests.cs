using WinCareDesktop.Services;
using WinCareDesktop.ViewModels;
using Xunit;
namespace WinCareDesktop.Tests;
public class RescueActionTests
{
    [Fact] public void Dashboard_navigation_cancels_pending_diagnostic_read()
    {
        UiTestHost.Run(()=>{
            var main=new MainViewModel();var pending=new TaskCompletionSource<DiagnosticSnapshot>();CancellationToken captured=default;
            var session=new DiagnosticSession(token=>{captured=token;return pending.Task;});
            var dashboard=new DashboardViewModel(main,session);var task=session.RescanAsync();
            try{dashboard.OnNavigatedFrom();Assert.True(captured.IsCancellationRequested);}
            finally{session.CancelScan();pending.SetCanceled();main.SchedulerVM.Dispose();}
        });
    }
    [Fact] public void Audit_explicit_fixture_path_persists_only_bounded_action_outcome()
    {
        var path=Path.Combine(Path.GetTempPath(),"actions-audit-"+Guid.NewGuid(),"audit.json");
        var audit=new RescueAudit(path);audit.Record("Quick","Confirmed start requested");audit.Record("Scan lifecycle","Unknown");
        var text=File.ReadAllText(path);Assert.Contains("Unknown",text);Assert.DoesNotContain("PID",text);Assert.DoesNotContain("Command",text);
    }
    [Fact] public void Window_close_cancels_new_monitoring_and_app_review()
    {
        UiTestHost.Run(()=>{
            var window=new WinCareDesktop.MainWindow();window.Show();var vm=(MainViewModel)window.DataContext;
            using var apps=new CancellationTokenSource();using var defender=new CancellationTokenSource();
            typeof(MemoryViewModel).GetField("_appCancel",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.SetValue(vm.MemoryVM,apps);
            typeof(DefenderViewModel).GetField("_actionCancel",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.SetValue(vm.DefenderVM,defender);
            window.Close();Assert.True(apps.IsCancellationRequested);Assert.True(defender.IsCancellationRequested);
        });
    }
    [Fact] public void Defender_has_explicit_scan_and_monitor_commands()
    {
        Assert.NotNull(typeof(DefenderViewModel).GetProperty("QuickScanCommand"));
        Assert.NotNull(typeof(DefenderViewModel).GetProperty("FullScanCommand"));
        Assert.NotNull(typeof(DefenderViewModel).GetProperty("MonitorCommand"));
    }
}
