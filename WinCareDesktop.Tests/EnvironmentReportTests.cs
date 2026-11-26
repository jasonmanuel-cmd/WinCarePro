using WinCareDesktop.Services;
using Xunit;

namespace WinCareDesktop.Tests;

public class EnvironmentReportTests
{
    [Fact]
    public void Report_labels_memory_maintenance_without_claiming_a_probe_or_standby_flush()
    {
        var report = EnvironmentReport.Get();
        Assert.Contains("Memory maintenance: WinCare working-set trim only (not probed)", report);
        Assert.DoesNotContain("Standby flush", report);
        Assert.DoesNotContain("MemoryCompression process", report);
    }

    [Fact]
    public void Report_uses_the_actual_safe_mode_history_path()
    {
        Assert.True(SafetyMode.Enabled);
        var report = EnvironmentReport.Get();
        Assert.Contains($"History file: {new HistoryStore().FilePath}", report);
    }
}
