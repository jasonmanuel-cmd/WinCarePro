using WinCareDesktop.Services;
using Xunit;

namespace WinCareDesktop.Tests;
public class DiagnosticScanTests
{
    [Fact]
    public void Freshness_poll_does_not_recreate_unchanged_guidance_and_steal_focus()
    {
        var session = new DiagnosticSession(_ => throw new InvalidOperationException());
        int changes = 0;
        session.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(session.Guidance)) changes++; };
        session.UpdateFreshness(); session.UpdateFreshness();
        Assert.Equal(0, changes);
    }

    [Fact]
    public async Task Cancelled_scan_never_publishes_late_result_and_blocks_overlap()
    {
        var source = new TaskCompletionSource<DiagnosticSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        var prior = new DiagnosticSnapshot(DateTimeOffset.UtcNow, 20, 30, 40, true, true);
        var session = new DiagnosticSession(_ => { calls++; return source.Task; }, prior);
        var scan = session.RescanAsync();
        Assert.True(session.IsScanning);
        await session.RescanAsync();
        Assert.Equal(1, calls);
        session.CancelScan();
        source.SetResult(prior with { RamPercent = 99 });
        await scan.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Same(prior, session.Snapshot);
        Assert.False(session.IsScanning);
        Assert.Contains("cancelled", session.ScanStatus);
    }

    [Fact]
    public async Task Failed_scan_keeps_previous_data_and_shows_sanitized_error()
    {
        var session = new DiagnosticSession(_ => throw new IOException("C:\\Users\\Private\\secret"));
        await session.RescanAsync();
        Assert.Null(session.Snapshot.CapturedAt);
        Assert.Contains("failed", session.ScanStatus);
        Assert.DoesNotContain("Private", session.ScanStatus);
        Assert.False(session.IsScanning);
    }

    [Fact]
    public async Task Successful_scan_updates_guidance_and_timestamp()
    {
        var snapshot = new DiagnosticSnapshot(DateTimeOffset.UtcNow, 95, 99, 1, true, true);
        var session = new DiagnosticSession(_ => Task.FromResult(snapshot));
        await session.RescanAsync();
        Assert.Same(snapshot, session.Snapshot);
        Assert.Equal(new[] { "disk", "memory" }, session.Guidance.Select(g => g.Id));
        Assert.Contains("complete", session.ScanStatus);
    }
}
