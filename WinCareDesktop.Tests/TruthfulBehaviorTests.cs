using WinCareDesktop.ViewModels;
using WinCareDesktop.Services;
using Xunit;
namespace WinCareDesktop.Tests;
public class TruthfulBehaviorTests
{
    [Fact]
    public void Network_and_disk_disclose_cumulative_and_partial_results()
    {
        UiTestHost.Run(() =>
        {
            var main = new MainViewModel();
            Assert.Contains("cumulative", main.NetworkMonitorVM.Subtitle, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("partial", main.DiskAnalyzerVM.Subtitle, StringComparison.OrdinalIgnoreCase);
        });
    }
    [Fact]
    public void Each_memory_request_executes_again_instead_of_caching_success()
    {
        var property = typeof(SystemOptimizerService).GetProperty("WorkingSetTrimmer");
        Assert.NotNull(property);
        int calls = 0;
        var svc = TestService.New();
        property.SetValue(svc, (Func<bool>)(() => { calls++; return true; }));
        svc.FlushStandbyMemory(); svc.FlushStandbyMemory();
        Assert.Equal(2, calls);
        Assert.DoesNotContain(svc.LogStream, line => line.Contains("Standby memory list flushed"));
    }
}
