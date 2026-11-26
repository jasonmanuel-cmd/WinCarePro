using WinCareDesktop.Services;
using Xunit;

namespace WinCareDesktop.Tests;

public class GuidanceTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 2, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(-61)]
    [InlineData(1)]
    public void Stale_or_future_evidence_is_not_presented_as_current(int seconds)
    {
        var rows = DashboardGuidance.Build(new(Now.AddSeconds(seconds), 99, 99, 1, false, false), Now);
        Assert.Equal("stale", Assert.Single(rows).Id);
    }

    [Fact]
    public void Missing_snapshot_requests_a_scan()
    {
        Assert.Equal("missing", Assert.Single(DashboardGuidance.Build(new(null, null, null, null, null, null), Now)).Id);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Invalid_metrics_are_unknown_not_pressure(double value)
    {
        var rows = DashboardGuidance.Build(new(Now, value, value, double.NaN, null, null), Now);
        Assert.Equal("unknown", Assert.Single(rows).Id);
    }

    [Fact]
    public void Healthy_snapshot_has_no_pressure_recommendation()
    {
        Assert.Equal("clear", Assert.Single(DashboardGuidance.Build(new(Now, 50, 70, 50, true, true), Now)).Id);
    }

    [Fact]
    public void Unknown_privacy_does_not_assert_it_is_on()
    {
        var rows = DashboardGuidance.Build(new(Now, 50, 70, 50, null, true), Now);
        Assert.Equal("unknown", Assert.Single(rows).Id);
    }

    [Fact]
    public void Disk_pressure_precedes_memory_and_optional_privacy_review()
    {
        var snapshot = new DiagnosticSnapshot(Now, 92, 96, 4, false, true);
        var rows = DashboardGuidance.Build(snapshot, Now);
        Assert.Equal(new[] { "disk", "memory", "privacy" }, rows.Select(r => r.Id));
        Assert.Equal("Disk Analyzer", rows[0].Panel);
        Assert.Contains("96%", rows[0].Title);
        Assert.Contains("92%", rows[1].Title);
        Assert.Contains("not a diagnosis", rows[1].Explanation);
        Assert.Equal(rows, DashboardGuidance.Build(snapshot, Now));
    }
}
