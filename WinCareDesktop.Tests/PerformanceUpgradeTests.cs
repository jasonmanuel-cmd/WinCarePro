using System.Diagnostics;
using System.IO;
using WinCareDesktop.Models;
using WinCareDesktop.Services;
using WinCareDesktop.ViewModels;
using Xunit;

namespace WinCareDesktop.Tests;

public class PerformanceUpgradeTests
{
    [Fact]
    public void Native_ram_stats_returns_valid_physical_memory_fast()
    {
        var svc = TestService.New();
        // Warm up P/Invoke
        _ = svc.GetRamStats();

        var sw = Stopwatch.StartNew();
        var (usedPercent, totalBytes, availableBytes) = svc.GetRamStats();
        sw.Stop();

        Assert.True(totalBytes > 0, "Expected positive total physical memory.");
        Assert.InRange(availableBytes, 0, totalBytes);
        Assert.InRange(usedPercent, 0, 100);
        Assert.True(sw.ElapsedMilliseconds < 50, $"Expected fast GlobalMemoryStatusEx read, took {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public void Top_memory_processes_reports_megabytes_not_gigabytes()
    {
        var svc = TestService.New();
        var top = svc.GetTopMemoryProcesses(5);
        Assert.NotEmpty(top);
        // The top process on a live Windows system uses well over 5 MB of RAM;
        // the old /1_073_741_824 divisor turned a 50 MB process into 0.05.
        Assert.True(top[0].WorkingSetMb > 5.0, $"Expected MB scale, got {top[0].WorkingSetMb}");
    }

    [Fact]
    public void Power_plan_switch_records_reversible_transaction_and_refuses_external_drift()
    {
        const string balanced = "381b4222-f694-41f0-9685-ff5bb260df2e";
        const string highPerf = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
        const string saver = "a1841308-3541-4fab-bc81-f71556f20b4a";

        var active = balanced;
        var svc = TestService.New();
        svc.IsPowerSafeMode = () => false;
        svc.ReadActivePowerGuid = () => active;
        svc.WriteActivePowerGuid = guid => { active = guid; return (true, "Applied fixture."); };

        var (ok, _) = svc.ApplyPowerPlan(highPerf, "High performance");
        Assert.True(ok);
        Assert.Equal(highPerf, active);

        var tx = Assert.Single(svc.GetUndoTransactions());
        Assert.Equal(UndoKind.PowerPlan, tx.Kind);
        Assert.True(tx.Reversable);

        // External drift must refuse overwrite
        active = saver;
        Assert.False(svc.TryRevert(tx, out var driftMsg));
        Assert.Contains("changed externally", driftMsg, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(saver, active);

        // Matching state restores exact prior scheme
        active = highPerf;
        Assert.True(svc.TryRevert(tx, out var okMsg));
        Assert.Contains("Restored", okMsg);
        Assert.Equal(balanced, active);
    }

    [Fact]
    public void Startup_toggle_records_undo_transaction_and_restores_cleanly()
    {
        var dir = Path.Combine(Path.GetTempPath(), "startup-undo-" + Guid.NewGuid());
        var registry = new Dictionary<string, string>
        {
            ["User Registry|Run|Discord"] = "\"C:\\Users\\Test\\AppData\\Local\\Discord\\Update.exe\" --processStart Discord.exe",
            ["User Registry|Run|LightweightHelper"] = "\"C:\\Tools\\helper.exe\""
        };
        var startupSvc = new StartupManagerService(dir, registry);
        var optimizer = TestService.New();
        optimizer.StartupUndoService = startupSvc;

        var vm = new StartupManagerViewModel(startupSvc) { History = optimizer };
        vm.Refresh();

        Assert.Equal(2, vm.DisplayedEntries.Count);
        // High-impact Discord is sorted ahead of Medium helper
        Assert.Equal("Discord", vm.DisplayedEntries[0].Name);
        Assert.Equal("High", vm.DisplayedEntries[0].Impact);

        // Disable Discord and verify undo transaction
        vm.ToggleItem(vm.DisplayedEntries[0]);
        Assert.False(startupSvc.GetStartupEntries().Single(e => e.Name == "Discord").IsEnabled);

        var tx = Assert.Single(optimizer.GetUndoTransactions());
        Assert.Equal(UndoKind.StartupEntry, tx.Kind);
        Assert.True(tx.Reversable);

        // Rollback re-enables Discord
        Assert.True(optimizer.TryRevert(tx, out _));
        Assert.True(startupSvc.GetStartupEntries().Single(e => e.Name == "Discord").IsEnabled);
    }

    [Fact]
    public void Memory_view_model_sorts_by_cpu_or_ram_and_filters_by_role()
    {
        var vm = new MemoryViewModel
        {
            Rows = new[]
            {
                new MemoryRow(10, "svchost", 120, 0.5),
                new MemoryRow(20, "chrome", 850, 2.0),
                new MemoryRow(30, "node", 95, 42.5),
            }
        };

        Assert.Contains("Windows System", vm.Rows[0].RoleHint);
        Assert.Contains("Browser", vm.Rows[1].RoleHint);

        // Default sort: RAM descending
        Assert.Equal(new[] { "chrome", "svchost", "node" }, vm.DisplayedRows.Select(r => r.Name));

        // Sort by CPU descending
        vm.SortByCpuLoad();
        Assert.Equal(new[] { "node", "chrome", "svchost" }, vm.DisplayedRows.Select(r => r.Name));

        // Filter by role hint
        vm.FilterText = "Browser";
        Assert.Equal("chrome", Assert.Single(vm.DisplayedRows).Name);
    }

    [Theory]
    [InlineData("VLC media player", true)]
    [InlineData("Spotify", true)]
    [InlineData("Discord", true)]
    [InlineData("Steam", true)]
    [InlineData("Epic Games Launcher", true)]
    [InlineData("Windows Security", false)]
    [InlineData("svchost", false)]
    [InlineData("unknown", false)]
    public void Expanded_optional_app_rules_allow_only_verified_optional_apps(string appName, bool expected)
    {
        var id = new OptionalAppIdentity(1234, 999, "C:\\App\\app.exe", "S-1-5-21-test", 1, 555, appName, true, false);
        Assert.Equal(expected, OptionalAppRules.Eligible(id, "S-1-5-21-test", 1));
    }
}
