using WinCareDesktop.Services;
using Xunit;

namespace WinCareDesktop.Tests;
public class DiagnosticReaderTests
{
    [Fact]
    public async Task Cancellation_between_reads_skips_later_probes()
    {
        using var cts = new CancellationTokenSource();
        int diskReads = 0;
        var reader = new DiagnosticReader(() => { cts.Cancel(); return (40d, 100L, 60L); },
            () => { diskReads++; return (50d, 50d, 100d); }, _ => true);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reader.ReadAsync(cts.Token));
        Assert.Equal(0, diskReads);
    }
    [Fact]
    public async Task Precancelled_scan_does_not_call_any_probe()
    {
        var reader = new DiagnosticReader(() => throw new Exception("Must not read"), () => (1d, 1d, 100d), _ => true);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reader.ReadAsync(new CancellationToken(true)));
    }
    [Fact]
    public async Task Missing_capacity_and_failed_privacy_are_unknown()
    {
        var reader = new DiagnosticReader(() => (0d, 0L, 0L), () => (0d, 0d, 0d), _ => throw new IOException("private path"));
        var result = await reader.ReadAsync(default);
        Assert.Null(result.RamPercent); Assert.Null(result.DiskPercent); Assert.Null(result.FreeDiskGb);
        Assert.Null(result.AdvertisingBlocked); Assert.Null(result.TailoredBlocked);
        Assert.NotNull(result.CapturedAt);
    }
    [Fact]
    public async Task Successful_probes_produce_detached_numeric_snapshot()
    {
        var reader = new DiagnosticReader(() => (40d, 100L, 60L), () => (70d, 70d, 100d), name => name == "Advertising ID");
        var result = await reader.ReadAsync(default);
        Assert.Equal(40, result.RamPercent); Assert.Equal(70, result.DiskPercent); Assert.Equal(30, result.FreeDiskGb);
        Assert.True(result.AdvertisingBlocked); Assert.False(result.TailoredBlocked);
    }
}
