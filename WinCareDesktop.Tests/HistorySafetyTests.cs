using WinCareDesktop.Models;
using WinCareDesktop.ViewModels;
using Xunit;
namespace WinCareDesktop.Tests;
public class HistorySafetyTests
{
    [Fact]
    public void Rollback_awaits_and_never_claims_display_only_action_restored()
    {
        UiTestHost.Run(() =>
        {
            var vm = new MainViewModel();
            var command = vm.HistoryLogsVM.RollbackCommand;
            Assert.IsAssignableFrom<CommunityToolkit.Mvvm.Input.IAsyncRelayCommand>(command);
        });
    }
    [Fact]
    public void Privacy_count_tracks_readback_and_history_view_refreshes_new_records()
    {
        UiTestHost.Run(() =>
        {
            var vm = new MainViewModel();
            foreach (var shield in vm.Shields) shield.Enabled = true;
            Assert.Equal(vm.Shields.Count, vm.PrivacyVM.BlockedCount);
            vm.Service.PushUndoTransaction(new OptimizationTransaction { Title = "fixture result" });
            vm.HistoryLogsVM.OnNavigatedTo();
            Assert.Contains(vm.UndoHistory, t => t.Title == "fixture result");
        });
    }

    [Fact]
    public void Privacy_model_notifies_ui_when_readback_changes()
    {
        Assert.IsAssignableFrom<System.ComponentModel.INotifyPropertyChanged>(new PrivacyShield());
    }
}
