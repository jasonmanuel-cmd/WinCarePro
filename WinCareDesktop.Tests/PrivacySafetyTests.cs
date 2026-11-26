using WinCareDesktop.ViewModels;
using Xunit;

namespace WinCareDesktop.Tests;
public class PrivacySafetyTests
{
    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(null, null)]
    [InlineData(7, null)]
    public void Privacy_only_claims_known_state_for_explicit_boolean_dwords(int? value, bool? blocked)
    {
        var svc = new WinCareDesktop.Services.SystemOptimizerService(new WinCareDesktop.Services.HistoryStore(TestService.NewHistoryPath()), _ => value, (_, _) => throw new Exception("No write expected"));
        Assert.Equal(blocked, svc.ReadPrivacyState("Advertising ID"));
    }

    [Fact]
    public void Privacy_read_failure_is_unknown_not_disabled_in_visible_model()
    {
        UiTestHost.Run(() =>
        {
            var vm = new MainViewModel();
            var field = typeof(WinCareDesktop.Services.SystemOptimizerService).GetField("_readPrivacy", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            field.SetValue(vm.Service, (Func<string, int?>)(_ => throw new UnauthorizedAccessException("fixture denied")));
            vm.RefreshPrivacyState();
            Assert.All(vm.Shields, shield => Assert.Equal("Unknown", shield.GetType().GetProperty("StateText")?.GetValue(shield)));
            Assert.Equal(0, vm.PrivacyVM.BlockedCount);
            Assert.Contains(vm.Service.BuildHealthFindings(50, 50, 100), text => text.Contains("unknown", StringComparison.OrdinalIgnoreCase));
            vm.SchedulerVM.Dispose();
        });
    }

    [Fact]
    public void Privacy_change_reads_back_and_undo_restores_absent_value()
    {
        var ctor = typeof(WinCareDesktop.Services.SystemOptimizerService).GetConstructor(new[] { typeof(WinCareDesktop.Services.HistoryStore), typeof(Func<string, int?>), typeof(Action<string, int?>) });
        Assert.NotNull(ctor);
        int? value = null;
        var svc = (WinCareDesktop.Services.SystemOptimizerService)ctor.Invoke(new object[] { new WinCareDesktop.Services.HistoryStore(TestService.NewHistoryPath()), (Func<string, int?>)(_ => value), (Action<string, int?>)((_, v) => value = v) });
        Assert.True(svc.ApplyPrivacySetting("Advertising ID", true));
        Assert.Equal(0, value);
        var tx = Assert.Single(svc.GetUndoTransactions());
        Assert.True(svc.TryRevert(tx, out _));
        Assert.Null(value);
    }

    [Fact]
    public void Privacy_refuses_mutation_if_history_cannot_persist_and_refuses_undo_conflict()
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "history-denied-" + Guid.NewGuid());
        System.IO.Directory.CreateDirectory(dir);
        int? value = 1;
        try
        {
            var blocked = new WinCareDesktop.Services.SystemOptimizerService(new WinCareDesktop.Services.HistoryStore(dir), _ => value, (_, v) => value = v);
            Assert.False(blocked.ApplyPrivacySetting("Advertising ID", true));
            Assert.Equal(1, value);
            var svc = new WinCareDesktop.Services.SystemOptimizerService(new WinCareDesktop.Services.HistoryStore(TestService.NewHistoryPath()), _ => value, (_, v) => value = v);
            Assert.True(svc.ApplyPrivacySetting("Advertising ID", true));
            value = 7; // external edit after WinCare's change
            Assert.False(svc.TryRevert(Assert.Single(svc.GetUndoTransactions()), out _));
            Assert.Equal(7, value);
        }
        finally { System.IO.Directory.Delete(dir, true); }
    }

    [Fact]
    public void Privacy_exposes_only_two_accurately_named_settings()
    {
        UiTestHost.Run(() =>
        {
            var vm = new MainViewModel();
            Assert.Equal(new[] { "Advertising ID", "Tailored Experiences" }, vm.Shields.Select(s => s.Name));
        });
    }
}
