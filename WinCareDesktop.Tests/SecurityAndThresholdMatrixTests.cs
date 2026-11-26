using System.IO;
using WinCareDesktop.Models;
using WinCareDesktop.Services;
using Xunit;

namespace WinCareDesktop.Tests;

public class SecurityAndThresholdMatrixTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0L, "0 B")]
    [InlineData(-1L, "0 B")]
    [InlineData(-999999L, "0 B")]
    [InlineData(1L, "1 B")]
    [InlineData(512L, "512 B")]
    [InlineData(1023L, "1023 B")]
    [InlineData(1024L, "1 KB")]
    [InlineData(1536L, "1.5 KB")]
    [InlineData(10240L, "10 KB")]
    [InlineData(1048575L, "1024 KB")]
    [InlineData(1048576L, "1 MB")]
    [InlineData(1572864L, "1.5 MB")]
    [InlineData(104857600L, "100 MB")]
    [InlineData(1073741824L, "1 GB")]
    [InlineData(1610612736L, "1.5 GB")]
    [InlineData(10737418240L, "10 GB")]
    [InlineData(1099511627776L, "1 TB")]
    [InlineData(2199023255552L, "2 TB")]
    public void FormatBytes_formats_exact_binary_units_across_scales(long bytes, string expected)
    {
        Assert.Equal(expected, InstalledProgram.FormatBytes(bytes));
    }

    [Theory]
    [InlineData(@"C:\Temp\WinCare", @"C:\Temp\WinCare\file.tmp", true)]
    [InlineData(@"C:\Temp\WinCare\", @"C:\Temp\WinCare\sub\a.log", true)]
    [InlineData(@"C:\Temp\WinCare", @"c:\temp\wincare\UPPER.TMP", true)]
    [InlineData(@"C:\Temp\WinCare", @"C:\Temp\WinCare", false)]
    [InlineData(@"C:\Temp\WinCare", @"C:\Temp\WinCare2\file.tmp", false)]
    [InlineData(@"C:\Temp\WinCare", @"C:\Temp\WinCare\..\outside.tmp", false)]
    [InlineData(@"C:\Temp\WinCare", @"C:\Temp\WinCare\sub\..\..\outside.tmp", false)]
    [InlineData(@"C:\Temp\WinCare", @"D:\Temp\WinCare\file.tmp", false)]
    public void PathSafety_IsWithin_blocks_sibling_prefix_and_parent_traversal(string root, string candidate, bool expected)
    {
        Assert.Equal(expected, PathSafety.IsWithin(candidate, root));
    }

    [Theory]
    [InlineData(0.0, 50.0, 100.0, true, true, "clear")]
    [InlineData(84.9, 89.9, 10.0, true, true, "clear")]
    [InlineData(85.0, 50.0, 50.0, true, true, "memory")]
    [InlineData(99.9, 50.0, 50.0, true, true, "memory")]
    [InlineData(100.0, 50.0, 50.0, true, true, "memory")]
    [InlineData(40.0, 90.0, 50.0, true, true, "disk")]
    [InlineData(40.0, 50.0, 9.9, true, true, "disk")]
    [InlineData(40.0, 50.0, 0.0, true, true, "disk")]
    [InlineData(40.0, 50.0, 50.0, false, true, "privacy")]
    [InlineData(40.0, 50.0, 50.0, true, false, "privacy")]
    [InlineData(40.0, 50.0, 50.0, false, false, "privacy")]
    [InlineData(-0.1, 50.0, 50.0, true, true, "unknown")]
    [InlineData(100.1, 50.0, 50.0, true, true, "unknown")]
    [InlineData(40.0, -0.1, 50.0, true, true, "unknown")]
    [InlineData(40.0, 100.1, 50.0, true, true, "unknown")]
    [InlineData(40.0, 50.0, -0.1, true, true, "unknown")]
    public void Guidance_threshold_boundaries_classify_exact_cutoffs(
        double ramPct, double diskPct, double freeGb, bool advBlocked, bool tailoredBlocked, string expectedFirstId)
    {
        var snap = new DiagnosticSnapshot(Now, ramPct, diskPct, freeGb, advBlocked, tailoredBlocked);
        var items = DashboardGuidance.Build(snap, Now);
        Assert.Equal(expectedFirstId, items[0].Id);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-30)]
    [InlineData(-59)]
    [InlineData(-60)]
    public void Guidance_within_60_seconds_is_accepted_as_fresh(int offsetSeconds)
    {
        var items = DashboardGuidance.Build(new(Now.AddSeconds(offsetSeconds), 40, 50, 80, true, true), Now);
        Assert.Equal("clear", Assert.Single(items).Id);
    }

    [Theory]
    [InlineData(-61)]
    [InlineData(-120)]
    [InlineData(-3600)]
    [InlineData(1)]
    [InlineData(60)]
    public void Guidance_outside_freshness_window_is_marked_stale(int offsetSeconds)
    {
        var items = DashboardGuidance.Build(new(Now.AddSeconds(offsetSeconds), 40, 50, 80, true, true), Now);
        Assert.Equal("stale", Assert.Single(items).Id);
    }

    [Theory]
    [InlineData(UndoKind.DisplayOnly, null, false)]
    [InlineData(UndoKind.DisplayOnly, "v1|anything", false)]
    [InlineData(UndoKind.ReinstallApp, null, false)]
    [InlineData(UndoKind.ReinstallApp, "Microsoft.Calculator", false)]
    [InlineData(UndoKind.DnsSettings, null, true)]
    [InlineData(UndoKind.DnsSettings, "{\"Before\":{}}", true)]
    [InlineData(UndoKind.PrivacyRegistry, null, false)]
    [InlineData(UndoKind.PrivacyRegistry, "", false)]
    [InlineData(UndoKind.PrivacyRegistry, "v2|Advertising ID|1|0", false)]
    [InlineData(UndoKind.PrivacyRegistry, "v3|Advertising ID|absent|0", true)]
    [InlineData(UndoKind.PowerPlan, null, false)]
    [InlineData(UndoKind.PowerPlan, "", false)]
    [InlineData(UndoKind.PowerPlan, "legacy|guid", false)]
    [InlineData(UndoKind.PowerPlan, "v1|381b4222-f694-41f0-9685-ff5bb260df2e|8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c", true)]
    [InlineData(UndoKind.StartupEntry, null, false)]
    [InlineData(UndoKind.StartupEntry, "", false)]
    [InlineData(UndoKind.StartupEntry, "v0|User Registry|Discord|True|cmd", false)]
    [InlineData(UndoKind.StartupEntry, "v1|User%20Registry|Discord|True|cmd", true)]
    public void OptimizationTransaction_Reversable_enforces_kind_and_payload_schema(
        UndoKind kind, string? payload, bool expectedReversible)
    {
        var tx = new OptimizationTransaction { Kind = kind, UndoPayload = payload };
        Assert.Equal(expectedReversible, tx.Reversable);
    }

    [Theory]
    [InlineData(0.0, true)]
    [InlineData(50.5, true)]
    [InlineData(100.0, true)]
    [InlineData(-0.001, false)]
    [InlineData(100.001, false)]
    [InlineData(double.NaN, false)]
    [InlineData(double.NegativeInfinity, false)]
    [InlineData(double.PositiveInfinity, false)]
    public void ValidPercent_accepts_only_finite_zero_to_hundred(double value, bool expected)
    {
        Assert.Equal(expected, DashboardGuidance.ValidPercent(value));
    }

    [Fact]
    public void Protected_windows_and_program_files_roots_always_throw_on_removal_validation()
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows))!,
        }.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var protectedPath in roots)
        {
            Assert.Throws<IOException>(() => PathSafety.ValidateRemoval(protectedPath));
        }
    }
}
