using System.ComponentModel;
using System.IO;
using WinCareDesktop.Models;
using WinCareDesktop.Services;
using Xunit;

namespace WinCareDesktop.Tests;

public class ServiceResilienceAndModelMatrixTests
{
    public static IEnumerable<object[]> TransactionCounts() =>
        Enumerable.Range(1, 16).Select(n => new object[] { n });

    [Theory]
    [MemberData(nameof(TransactionCounts))]
    public void HistoryStore_persists_exact_count_and_monotonic_unique_ids(int count)
    {
        var path = Path.Combine(Path.GetTempPath(), $"hist-{Guid.NewGuid()}.json");
        try
        {
            var store = new HistoryStore(path);
            var svc = new SystemOptimizerService(store, _ => 0, (_, _) => { });

            for (var i = 0; i < count; i++)
            {
                svc.PushUndoTransaction(new OptimizationTransaction
                {
                    Title = $"Action {i}",
                    Kind = UndoKind.DisplayOnly,
                    ReclaimedGb = i * 0.25
                });
            }

            var reloaded = new SystemOptimizerService(new HistoryStore(path), _ => 0, (_, _) => { });
            reloaded.LoadUndoHistory();
            var list = reloaded.GetUndoTransactions();

            Assert.Equal(count, list.Count);
            Assert.Equal(count, list.Select(x => x.Id).Distinct().Count());
            // Newest transaction sits at index 0 with highest ID
            Assert.Equal(count, list[0].Id);
            Assert.Equal(1, list[^1].Id);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Theory]
    [InlineData(20, 40, 120, true, "RAM is fine", "Disk has room", "already blocked")]
    [InlineData(84.9, 89.9, 20.0, true, "RAM is fine", "Disk has room", "already blocked")]
    [InlineData(85.0, 40, 120, true, "RAM is high", "Disk has room", "already blocked")]
    [InlineData(95.0, 40, 120, false, "RAM is high", "Disk has room", "still on")]
    [InlineData(40, 90.0, 50, true, "RAM is fine", "Disk is tight", "already blocked")]
    [InlineData(40, 50.0, 19.9, true, "RAM is fine", "Disk is tight", "already blocked")]
    [InlineData(92.0, 95.0, 5.0, false, "RAM is high", "Disk is tight", "still on")]
    [InlineData(50.0, 50.0, 80.0, null, "RAM is fine", "Disk has room", "unknown")]
    public void BuildHealthFindings_reports_accurate_ram_disk_and_privacy_messages(
        double ramPct, double storagePct, double freeGb, bool? advBlocked,
        string expectedRamFragment, string expectedDiskFragment, string expectedPrivacyFragment)
    {
        int? rawPrivacy = advBlocked switch { true => 0, false => 1, null => null };
        var path = Path.Combine(Path.GetTempPath(), $"find-{Guid.NewGuid()}.json");
        try
        {
            var svc = new SystemOptimizerService(new HistoryStore(path), _ => rawPrivacy, (_, _) => { });
            var findings = svc.BuildHealthFindings(ramPct, storagePct, freeGb);
            Assert.Equal(4, findings.Count);
            Assert.Contains(expectedRamFragment, findings[0]);
            Assert.Contains(expectedDiskFragment, findings[1]);
            Assert.Contains(expectedPrivacyFragment, findings[2]);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Theory]
    [InlineData(true, true, "Protected (off)")]
    [InlineData(false, false, "Not protected (on)")]
    [InlineData(null, false, "Unknown")]
    public void PrivacyShield_observable_state_notifies_dependent_properties(bool? blocked, bool expectedEnabled, string expectedState)
    {
        var shield = new PrivacyShield { Name = "Advertising ID", Blocked = !blocked.GetValueOrDefault(true) };
        var changed = new List<string>();
        shield.PropertyChanged += (_, e) => { if (e.PropertyName != null) changed.Add(e.PropertyName); };

        shield.Blocked = blocked;
        Assert.Equal(expectedEnabled, shield.Enabled);
        Assert.Equal(expectedState, shield.StateText);
        Assert.Contains(nameof(PrivacyShield.Blocked), changed);
        Assert.Contains(nameof(PrivacyShield.Enabled), changed);
        Assert.Contains(nameof(PrivacyShield.StateText), changed);
    }

    [Theory]
    [InlineData("1.1.1.1", "Cloudflare", true)]
    [InlineData("8.8.8.8", "Google", true)]
    [InlineData("9.9.9.9", "Quad9", true)]
    public void DnsResolver_property_notifications_fire_only_on_distinct_changes(string ip, string name, bool targetActive)
    {
        var dns = new DnsResolver { Id = name.ToLowerInvariant(), Name = name, Ip = ip };
        var events = 0;
        dns.PropertyChanged += (_, _) => events++;

        dns.Active = targetActive;
        dns.Active = targetActive; // duplicate assignment must not fire again
        Assert.Equal(1, events);

        dns.Ping = "12 ms";
        dns.Ping = "12 ms";
        Assert.Equal(2, events);
    }

    [Theory]
    [InlineData(-60)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(10)]
    [InlineData(14)]
    [InlineData(16)]
    [InlineData(20)]
    [InlineData(29)]
    [InlineData(31)]
    [InlineData(45)]
    [InlineData(59)]
    [InlineData(61)]
    [InlineData(120)]
    [InlineData(3600)]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    public void RescueOptionsStore_rejects_unsupported_refresh_intervals(int invalidSeconds)
    {
        var path = Path.Combine(Path.GetTempPath(), $"opt-bad-{Guid.NewGuid()}.json");
        var store = new RescueOptionsStore(path);
        Assert.Throws<ArgumentException>(() => store.Save(new RescueOptions(RefreshSeconds: invalidSeconds)));
        Assert.False(File.Exists(path));
    }

    [Theory]
    [InlineData(5, false)]
    [InlineData(5, true)]
    [InlineData(15, false)]
    [InlineData(15, true)]
    [InlineData(30, false)]
    [InlineData(30, true)]
    [InlineData(60, false)]
    [InlineData(60, true)]
    public void RescueOptionsStore_roundtrips_every_supported_interval_and_diagnostics_flag(int seconds, bool localDiag)
    {
        var path = Path.Combine(Path.GetTempPath(), $"opt-ok-{Guid.NewGuid()}.json");
        try
        {
            var store = new RescueOptionsStore(path);
            store.Save(new RescueOptions(RefreshSeconds: seconds, ReducedMotion: true, LocalDiagnostics: localDiag));
            var loaded = store.Load();
            Assert.Equal(seconds, loaded.Value.RefreshSeconds);
            Assert.True(loaded.Value.ReducedMotion);
            Assert.Equal(localDiag, loaded.Value.LocalDiagnostics);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Theory]
    [InlineData("Advertising ID", null, true, 0)]
    [InlineData("Advertising ID", 1, true, 0)]
    [InlineData("Advertising ID", 0, false, 1)]
    [InlineData("Tailored Experiences", null, true, 0)]
    [InlineData("Tailored Experiences", 1, true, 0)]
    [InlineData("Tailored Experiences", 0, false, 1)]
    public void Privacy_setting_apply_and_revert_restores_exact_prior_value_or_absence(
        string setting, int? initialValue, bool protect, int expectedWritten)
    {
        var state = new Dictionary<string, int?> { [setting] = initialValue };
        var path = Path.Combine(Path.GetTempPath(), $"priv-{Guid.NewGuid()}.json");
        try
        {
            var svc = new SystemOptimizerService(
                new HistoryStore(path),
                k => state.TryGetValue(k, out var v) ? v : null,
                (k, v) => state[k] = v);

            Assert.True(svc.ApplyPrivacySetting(setting, protect));
            Assert.Equal(expectedWritten, state[setting]);

            var tx = Assert.Single(svc.GetUndoTransactions());
            Assert.True(tx.Reversable);
            Assert.True(svc.TryRevert(tx, out _));
            Assert.Equal(initialValue, state[setting]);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    public static IEnumerable<object?[]> ExportSnapshots()
    {
        var ramValues = new double?[] { null, 0.0, 42.5, 85.0, 99.9, double.NaN };
        var diskValues = new double?[] { null, 55.0, 91.0, 100.0 };
        foreach (var r in ramValues)
        {
            foreach (var d in diskValues)
            {
                yield return new object?[] { r, d, d.HasValue ? (100.0 - d.Value) : null, true, false };
            }
        }
    }

    [Theory]
    [MemberData(nameof(ExportSnapshots))]
    public void DiagnosticExport_formats_sanitized_json_without_machine_or_user_identifiers(
        double? ram, double? disk, double? freeGb, bool? adv, bool? tailored)
    {
        var now = new DateTimeOffset(2026, 11, 25, 8, 30, 0, TimeSpan.Zero);
        var snap = new DiagnosticSnapshot(ram.HasValue ? now : null, ram, disk, freeGb, adv, tailored);
        var json = DiagnosticExport.Preview(snap, now);

        Assert.False(string.IsNullOrWhiteSpace(json));
        Assert.Contains("wincare-diagnostics-v1", json);
        Assert.DoesNotContain(Environment.UserName, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Environment.MachineName, json, StringComparison.OrdinalIgnoreCase);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal(System.Text.Json.JsonValueKind.Object, doc.RootElement.ValueKind);
    }

    public static IEnumerable<object[]> RamUsageSteps() =>
        Enumerable.Range(0, 21).Select(i => new object[] { i * 5.0 });

    [Theory]
    [MemberData(nameof(RamUsageSteps))]
    public void MainViewModel_score_display_and_ram_usage_format_consistently_across_0_to_100(double ramPct)
    {
        UiTestHost.Run(() =>
        {
            var vm = new WinCareDesktop.ViewModels.MainViewModel
            {
                TotalRamGb = 16.0,
                AvailableRamGb = Math.Round(16.0 * (100.0 - ramPct) / 100.0, 1),
                TotalStorageGb = 512.0,
                FreeStorageGb = 256.0,
                RamPercent = ramPct,
                StoragePercent = 50.0
            };
            try
            {
                Assert.Equal($"{ramPct:F0}%", vm.RamUsageText);
                Assert.Contains("available", vm.RamCapacityText);
                Assert.NotEqual("—", vm.ScoreDisplay);
            }
            finally
            {
                vm.SchedulerVM.Dispose();
            }
        });
    }

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1024, "1 KB")]
    [InlineData(52428800, "50 MB")]
    [InlineData(2147483648, "2 GB")]
    public void InstalledProgram_DisplaySize_respects_SizeKnown_flag(long sizeBytes, string formatted)
    {
        var unknown = new InstalledProgram { Name = "App", SizeBytes = sizeBytes, SizeKnown = false };
        Assert.Equal("size unknown", unknown.DisplaySize);

        var known = new InstalledProgram { Name = "App", SizeBytes = sizeBytes, SizeKnown = true };
        Assert.Equal(formatted, known.DisplaySize);
    }

    [Fact]
    public void Log_stream_caps_at_200_entries_to_prevent_unbounded_memory_growth()
    {
        var svc = TestService.New();
        for (var i = 0; i < 260; i++)
        {
            svc.Log($"Entry {i}");
        }
        Assert.Equal(200, svc.LogStream.Count);
        Assert.Contains("Entry 259", svc.LogStream[^1]);
    }
}
