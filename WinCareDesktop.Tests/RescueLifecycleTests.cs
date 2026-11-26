using System.IO;
using WinCareDesktop.Services;
using WinCareDesktop.ViewModels;
using Xunit;
namespace WinCareDesktop.Tests;
public class RescueLifecycleTests
{
    [Fact] public async Task Memory_cancellation_discards_late_result_and_blocks_duplicate_read()
    {
        var pending=new TaskCompletionSource<MemoryReading>(); int calls=0;
        var vm=new MemoryViewModel { Read=_=>{calls++;return pending.Task;} };
        var task=vm.RefreshAsync(); await vm.RefreshAsync(); Assert.Equal(1,calls);Assert.True(vm.Busy);
        vm.OnNavigatedFrom(); pending.SetResult(new(DateTimeOffset.UtcNow,100,90,new[]{new MemoryRow(123,"fixture",5,1)},1,0));
        await task.WaitAsync(TimeSpan.FromSeconds(5)); Assert.Empty(vm.Rows);Assert.False(vm.Busy); Assert.Contains("cancelled",vm.Status);
    }
    [Fact] public async Task Memory_failure_is_visible_without_private_exception_text()
    {
        var vm=new MemoryViewModel { Read=_=>throw new IOException("private path")};
        await vm.RefreshAsync();Assert.Contains("failed",vm.Status);Assert.DoesNotContain("private path",vm.Status);Assert.False(vm.Busy);
    }
    [Fact] public async Task Defender_cancellation_is_not_reported_as_scan_cancellation_or_completion()
    {
        var pending=new TaskCompletionSource<DefenderReading>();int calls=0;
        var vm=new DefenderViewModel {Read=_=>{calls++;return pending.Task;}};
        var task=vm.RefreshAsync();await vm.RefreshAsync();Assert.Equal(1,calls);
        vm.OnNavigatedFrom();pending.SetResult(new("Normal",true,true,null,"fixture"));await task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.StartsWith("Not read",vm.Protection);Assert.Contains("no Defender scan was cancelled",vm.Status);Assert.False(vm.Busy);
    }
    [Fact] public async Task Defender_error_clears_previous_protection_not_clean()
    {
        var vm=new DefenderViewModel {Read=_=>Task.FromResult(new DefenderReading("Normal",true,true,null,"fixture"))};
        await vm.RefreshAsync();Assert.StartsWith("Active",vm.Protection);
        vm.Read=_=>throw new IOException("private details");await vm.RefreshAsync();
        Assert.StartsWith("Unavailable or error",vm.Protection);Assert.DoesNotContain("private details",vm.Status);
    }
    [Fact] public void Root_safe_mode_does_not_call_confirmation_or_shell()
    {
        int calls=0; var vm=new DefenderViewModel {ConfirmOpen=()=>{calls++;return true;},LaunchSecurity=()=>calls++};
        vm.OpenSecurity(); Assert.Equal(0,calls);Assert.False(vm.CanOpenSecurity);
    }
    [Fact] public void Safe_options_save_and_reset_never_write_even_injected_store()
    {
        var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".json");
        var vm=new OptionsViewModel(new(path));vm.RefreshSeconds=60;vm.Save();vm.Reset();
        Assert.False(File.Exists(path));Assert.Equal(15,vm.Applied.RefreshSeconds);Assert.Contains("safe mode",vm.Status);
    }
    [Fact] public async Task Precancelled_native_reads_do_not_query_windows()
    {
        using var cts=new CancellationTokenSource();cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>NativeMemoryReader.ReadAsync(cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>DefenderStatusReader.ReadAsync(cts.Token));
    }
}
