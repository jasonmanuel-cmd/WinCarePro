using WinCareDesktop.Models;
using WinCareDesktop.ViewModels;
using Xunit;

namespace WinCareDesktop.Tests;
public class UninstallSafetyTests
{
    [Fact]
    public async Task Store_inventory_failure_discloses_partial_and_keeps_classic_rows()
    {
        var vm = new AppUninstallViewModel { LoadPrograms = () => new() { new InstalledProgram { Name = "Classic fixture" } } };
        var store = typeof(AppUninstallViewModel).GetProperty("LoadStorePrograms");
        Assert.NotNull(store);
        store.SetValue(vm, (Func<List<InstalledProgram>>)(() => throw new System.IO.IOException("Store fixture denied")));
        await vm.LoadAppsAsync();
        Assert.Single(vm.DisplayedApps);
        Assert.Contains("partial", vm.StatusText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Store fixture denied", vm.StatusText);
    }

    [Theory]
    [InlineData(1, "", "access denied")]
    [InlineData(0, "{broken json", "")]
    [InlineData(0, "[]", "partial enumeration error")]
    public void Store_scan_failure_is_not_a_successful_empty_inventory(int code, string output, string error)
    {
        var svc = new WinCareDesktop.Services.AppxService(TestService.New());
        var read = typeof(WinCareDesktop.Services.AppxService).GetProperty("ReadInventoryProcess");
        Assert.NotNull(read);
        read.SetValue(svc, (Func<string, string, int, (int, string, string)>)((_, _, _) => (code, output, error)));
        Assert.Throws<System.IO.IOException>(() => svc.GetStoreApps());
    }

    [Theory]
    [InlineData(0, false, "VerifiedRemoved")]
    [InlineData(0, true, "Pending")]
    [InlineData(0, null, "Unknown")]
    [InlineData(3010, false, "RebootRequired")]
    [InlineData(3010, true, "RebootRequired")]
    [InlineData(1641, true, "RebootRequired")]
    [InlineData(1603, true, "Failed")]
    public void Classic_outcomes_distinguish_pending_unknown_and_reboot(int exit, bool? stillInstalled, string expected)
    {
        var type = typeof(AppUninstallViewModel).Assembly.GetType("WinCareDesktop.Models.UninstallOutcome");
        Assert.NotNull(type);
        var result = type.GetMethod("FromClassicExit")!.Invoke(null, new object?[] { exit, stillInstalled })!;
        Assert.Equal(expected, type.GetProperty("State")!.GetValue(result)!.ToString());
        Assert.Equal(expected == "VerifiedRemoved", type.GetProperty("Success")!.GetValue(result));
    }

    [Fact]
    public async Task Mixed_uninstall_keeps_pending_unknown_reboot_and_failed_rows_with_distinct_counts()
    {
        var vm = new AppUninstallViewModel { Confirm = _ => true, History = TestService.New() };
        var execute = typeof(AppUninstallViewModel).GetProperty("UninstallWithOutcome");
        Assert.NotNull(execute);
        execute.SetValue(vm, (Func<InstalledProgram, CancellationToken, Task<UninstallOutcome>>)((app, _) => Task.FromResult(new UninstallOutcome(Enum.Parse<UninstallState>(app.Name), "fixture result"))));
        foreach (var state in Enum.GetValues<UninstallState>())
            vm.DisplayedApps.Add(new SelectableApp(new InstalledProgram { Name = state.ToString() }) { IsSelected = true });
        await vm.UninstallSelectedAsync();
        Assert.Equal(4, vm.DisplayedApps.Count);
        Assert.DoesNotContain(vm.DisplayedApps, a => a.Name == "VerifiedRemoved");
        Assert.Contains("1 verified removed", vm.StatusText);
        Assert.Contains("1 pending", vm.StatusText);
        Assert.Contains("1 reboot required", vm.StatusText);
        Assert.Contains("1 unknown", vm.StatusText);
        Assert.Contains("1 failed", vm.StatusText);
        Assert.Contains(vm.History.GetUndoTransactions(), tx => tx.Description.Contains("RebootRequired"));
    }

    [Fact]
    public void Appx_requires_verified_removal_marker_not_merely_exit_zero()
    {
        var ctor = typeof(WinCareDesktop.Services.AppxService).GetConstructor(new[] { typeof(WinCareDesktop.Services.SystemOptimizerService), typeof(Func<string, string, int, (int, string, string)>) });
        Assert.NotNull(ctor);
        Func<string, string, int, (int, string, string)> runner = (_, _, _) => (0, "", "");
        var svc = (WinCareDesktop.Services.AppxService)ctor.Invoke(new object[] { TestService.New(), runner });
        Assert.False(svc.RemoveStoreApp("Vendor.App_1.0.0.0_x64__abc", "Example").Success);
    }

    [Fact]
    public async Task Inventory_includes_store_apps_without_truncation()
    {
        var property = typeof(AppUninstallViewModel).GetProperty("LoadPrograms");
        Assert.NotNull(property);
        var vm = new AppUninstallViewModel();
        property.SetValue(vm, (Func<List<InstalledProgram>>)(() => Enumerable.Range(1, 200).Select(i => new InstalledProgram { Name = "App " + i, Kind = i == 200 ? UninstallKind.Appx : UninstallKind.Classic }).ToList()));
        await vm.LoadAppsAsync();
        Assert.Equal(200, vm.DisplayedApps.Count);
        Assert.Contains(vm.DisplayedApps, p => p.Program.Kind == UninstallKind.Appx);
    }

    [Fact]
    public void Legacy_uninstall_history_does_not_offer_unverified_reinstall()
    {
        Assert.False(new OptimizationTransaction { Kind = UndoKind.ReinstallApp, UndoPayload = "old-package" }.Reversable);
    }

    [Fact]
    public void Msi_install_registration_is_converted_to_actual_uninstall()
    {
        var app = new InstalledProgram { UninstallString = "MsiExec.exe /I{12345678-1234-1234-1234-123456789ABC}" };
        Assert.True(WinCareDesktop.Services.InstalledProgramsService.TryBuildUninstallCommand(app, out _, out var args, out _));
        Assert.StartsWith("/x {12345678-1234-1234-1234-123456789ABC}", args);
    }

    [Fact]
    public async Task Failed_uninstall_is_retained_and_not_reported_successful()
    {
        var vm = new AppUninstallViewModel();
        typeof(AppUninstallViewModel).GetProperty("Confirm")?.SetValue(vm, (Func<string, bool>)(_ => true));
        typeof(AppUninstallViewModel).GetProperty("History")?.SetValue(vm, TestService.New());
        var app = new SelectableApp(new InstalledProgram { Name = "No uninstaller", Kind = UninstallKind.Classic }) { IsSelected = true };
        vm.DisplayedApps.Add(app);
        await vm.UninstallSelectedAsync();
        Assert.Contains(app, vm.DisplayedApps);
        Assert.Contains("1 failed", vm.StatusText);
    }
}
