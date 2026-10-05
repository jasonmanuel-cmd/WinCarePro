using WinCareDesktop.Models;
using WinCareDesktop.Services;
using Xunit;

namespace WinCareDesktop.Tests;

public class DnsMatchingTests
{
    private static readonly (string name, string ip)[] Known =
    {
        ("Cloudflare", "1.1.1.1"),
        ("Google", "8.8.8.8"),
        ("Quad9", "9.9.9.9"),
    };

    [Fact]
    public void Known_ip_matches_by_name()
    {
        var svc = TestService.New();
        var (name, ip, matched) = svc.MatchCurrentDns(Known);
        // Either a known resolver matched, or the system is on something else.
        if (matched) Assert.Contains(name, new[] { "Cloudflare", "Google", "Quad9" });
        else Assert.True(string.IsNullOrEmpty(ip) || name.StartsWith("Custom"));
    }

    [Fact]
    public void Empty_input_does_not_throw()
    {
        var svc = TestService.New();
        var result = svc.MatchCurrentDns(Array.Empty<(string, string)>());
        Assert.NotNull(result.name);
    }

    [Fact]
    public void Current_dns_string_is_never_null()
    {
        var svc = TestService.New();
        Assert.NotNull(svc.CurrentDns);
    }
}

public class DnsResolverTests
{
    [Fact]
    public void Active_raises_PropertyChanged()
    {
        var r = new DnsResolver { Active = false };
        var fired = new List<string?>();
        r.PropertyChanged += (_, e) => fired.Add(e.PropertyName);

        r.Active = true;

        Assert.Contains(nameof(DnsResolver.Active), fired);
    }

    [Fact]
    public void Setting_same_value_does_not_raise()
    {
        var r = new DnsResolver { Active = true };
        var count = 0;
        r.PropertyChanged += (_, _) => count++;

        r.Active = true;

        Assert.Equal(0, count);
    }

    [Fact]
    public void Ping_raises_PropertyChanged()
    {
        var r = new DnsResolver();
        var fired = false;
        r.PropertyChanged += (_, e) => fired |= e.PropertyName == nameof(DnsResolver.Ping);
        r.Ping = "12 ms";
        Assert.True(fired);
    }
}

public class ServiceSafetyTests
{
    [Fact]
    public void Temp_scan_does_not_delete_anything()
    {
        var svc = TestService.New();
        var probeDir = Path.Combine(Path.GetTempPath(), "wincare_probe_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(probeDir);
        var probe = Path.Combine(probeDir, "probe.txt");
        File.WriteAllText(probe, "hello");

        try
        {
            var results = svc.ScanTempForCleanup();

            // The scan must be side-effect free: our probe is still on disk.
            Assert.True(File.Exists(probe), "ScanTempForCleanup deleted a file during a read-only scan");

            // A read-only scan is EXPECTED to report the probe. Getting it back
            // is the correct behaviour; what matters is that nothing was deleted.
            Assert.Contains(results, c => c.Path == probeDir);
        }
        finally
        {
            try { Directory.Delete(probeDir, true); } catch { }
        }
    }

    [Fact]
    public void Temp_roots_are_never_deleted()
    {
        var svc = TestService.New();
        svc.ScanTempForCleanup();
        Assert.True(Directory.Exists(Path.GetTempPath()),
            "A temp root was removed. This is the destructive regression.");
    }

    [Fact]
    public void Scan_returns_items_sorted_largest_first()
    {
        var svc = TestService.New();
        var results = svc.ScanTempForCleanup();
        for (var i = 1; i < results.Count; i++)
            Assert.True(results[i - 1].Bytes >= results[i].Bytes);
    }

    [Fact]
    public void Undo_history_survives_service_restart()
    {
        var svc = TestService.New();
        var before = svc.GetUndoTransactions().Count;

        svc.PushUndoTransaction(new OptimizationTransaction
        {
            Title = "test entry",
            Kind = UndoKind.DisplayOnly
        });

        Assert.Equal(before + 1, svc.GetUndoTransactions().Count);
        Assert.Equal("test entry", svc.GetUndoTransactions().First().Title);
    }

    [Fact]
    public void Push_assigns_unique_increasing_ids()
    {
        var svc = TestService.New();
        svc.LoadUndoHistory();
        svc.PushUndoTransaction(new OptimizationTransaction { Title = "a" });
        svc.PushUndoTransaction(new OptimizationTransaction { Title = "b" });

        var ids = svc.GetUndoTransactions().Select(t => t.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.Equal("b", svc.GetUndoTransactions().First().Title);
    }

    [Fact]
    public void Display_only_revert_reports_nothing_restored()
    {
        var svc = TestService.New();
        var ok = svc.TryRevert(new OptimizationTransaction { Kind = UndoKind.DisplayOnly }, out var msg);
        Assert.False(ok);
        Assert.Contains("cannot be recovered", msg, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Display_only_history_entry_is_not_marked_reversible()
    {
        var svc = TestService.New();
        svc.PushUndoTransaction(new OptimizationTransaction { Title = "cleanup", Kind = UndoKind.DisplayOnly });
        var tx = svc.GetUndoTransactions().First(t => t.Title == "cleanup");
        Assert.False(tx.Reversable);
    }

    [Fact]
    public void Bloatware_scan_only_returns_curated_entries()
    {
        var svc = TestService.New();
        var apps = svc.GetUnusedApps();

        // Nothing should come back that we would never actually suggest removing.
        Assert.DoesNotContain(apps, a => a.Name is "Photos" or "Calculator");
        Assert.All(apps, a => Assert.False(string.IsNullOrWhiteSpace(a.Reason)));
    }

    [Fact]
    public void Battery_returns_minus_one_on_a_desktop()
    {
        var svc = TestService.New();
        var pct = svc.GetBatteryPercent();
        Assert.True(pct == -1 || pct >= 0, "battery value should be -1 (absent) or a real percentage");
    }

    [Fact]
    public void Disk_health_never_throws_when_unavailable()
    {
        var svc = TestService.New();
        var health = svc.GetDiskHealth();
        Assert.NotNull(health.Status);
    }

    [Fact]
    public void Top_processes_returns_bounded_list()
    {
        var svc = TestService.New();
        var procs = svc.GetTopMemoryProcesses(5);
        Assert.True(procs.Count <= 5);
        Assert.All(procs, p => Assert.True(p.WorkingSetMb >= 0));
    }

    [Fact]
    public void Uptime_is_non_negative_or_zero()
    {
        var svc = TestService.New();
        Assert.True(svc.GetLastBootTime() >= TimeSpan.Zero);
    }

    [Fact]
    public void Health_findings_always_returns_entries()
    {
        var svc = TestService.New();
        var findings = svc.BuildHealthFindings(50, 50, 100);
        Assert.NotEmpty(findings);
    }
}