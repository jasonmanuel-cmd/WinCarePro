using System.IO;
using WinCareDesktop.Services;
using Xunit;
namespace WinCareDesktop.Tests;
public class SchedulerLifecycleTests
{
    [Fact]
    public async Task Scheduler_cancel_and_reenable_cannot_replace_an_active_run()
    {
        var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var service = new SchedulerService(TestService.NewHistoryPath(), token => { entered.SetResult(token); return finish.Task; });
        var now = new DateTime(2026, 1, 1, 1, 0, 0);
        var config = new ScheduleConfig { TimeOfDay = "02:00" };
        service.EnableForSession(config, now);
        var tick = service.TickAsync(now.AddHours(1));
        var token = await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await service.TickAsync(now.AddDays(3)).WaitAsync(TimeSpan.FromSeconds(3)); // No overlapping runner.
        service.Disable();
        try
        {
            Assert.True(token.IsCancellationRequested);
            Assert.Throws<InvalidOperationException>(() => service.EnableForSession(config, now.AddHours(2)));
        }
        finally { finish.TrySetResult("old run finished"); await tick.WaitAsync(TimeSpan.FromSeconds(3)); }
        Assert.Null(service.NextRun);
        Assert.Contains("Disabled", service.Status);
        service.EnableForSession(config, now.AddHours(2));
        Assert.NotNull(service.NextRun);
    }

    [Fact]
    public async Task Fake_clock_reversal_sleep_and_duplicate_ticks_do_not_replay_slots()
    {
        int runs = 0;
        using var service = new SchedulerService(TestService.NewHistoryPath(), _ => { runs++; return Task.FromResult("fixture only"); });
        var now = new DateTime(2026, 1, 1, 1, 0, 0);
        service.EnableForSession(new ScheduleConfig { TimeOfDay = "02:00" }, now);
        await service.TickAsync(now.AddHours(1));
        await service.TickAsync(now.AddDays(-1));
        await service.TickAsync(now.AddHours(1));
        Assert.Equal(1, runs);
        await service.TickAsync(now.AddDays(30));
        await service.TickAsync(now.AddDays(30));
        Assert.Equal(2, runs); // One resumed attempt, never a missed-day queue.
        service.Disable();
        await service.TickAsync(now.AddDays(40));
        Assert.Equal(2, runs);
    }

    [Fact]
    public async Task Disposed_scheduler_cancels_active_fake_runner_and_cannot_rearm()
    {
        var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new SchedulerService(TestService.NewHistoryPath(), async token =>
        {
            entered.SetResult(token);
            await Task.Delay(Timeout.Infinite, token);
            return "unreachable";
        });
        var now = new DateTime(2026, 1, 1, 1, 0, 0);
        var config = new ScheduleConfig { TimeOfDay = "02:00" };
        service.EnableForSession(config, now);
        var tick = service.TickAsync(now.AddHours(1));
        var token = await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        service.Dispose();
        service.Dispose(); // Window teardown and owner disposal may both call this.
        await tick.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(token.IsCancellationRequested);
        Assert.Null(service.NextRun);
        Assert.Throws<ObjectDisposedException>(() => service.EnableForSession(config, now));
    }

    [Fact]
    public void Scheduler_enable_requires_explicit_confirmation()
    {
        UiTestHost.Run(() =>
        {
            var vm = new WinCareDesktop.ViewModels.SchedulerViewModel(new SchedulerService(Path.Combine(Path.GetTempPath(), "vm-schedule-" + Guid.NewGuid() + ".json")));
            typeof(WinCareDesktop.ViewModels.SchedulerViewModel).GetProperty("Confirm")?.SetValue(vm, (Func<string, bool>)(_ => false));
            vm.ToggleEnabled();
            Assert.False(vm.Config.IsEnabled);
        });
    }

    [Fact]
    public async Task Explicit_consent_runs_once_when_due_not_on_duplicate_ticks()
    {
        var type = typeof(SchedulerService);
        var ctor = type.GetConstructor(new[] { typeof(string), typeof(Func<CancellationToken, Task<string>>) });
        Assert.NotNull(ctor);
        int runs = 0;
        Func<CancellationToken, Task<string>> run = _ => { runs++; return Task.FromResult("fixture cleaned"); };
        var service = (SchedulerService)ctor.Invoke(new object[] { Path.Combine(Path.GetTempPath(), "schedule-" + Guid.NewGuid() + ".json"), run });
        var now = new DateTime(2026, 1, 1, 1, 0, 0);
        type.GetMethod("EnableForSession")!.Invoke(service, new object[] { new ScheduleConfig { Frequency = "Daily", TimeOfDay = "02:00", CleanTempFiles = true }, now });
        await (Task)type.GetMethod("TickAsync")!.Invoke(service, new object[] { now })!;
        Assert.Equal(0, runs);
        await (Task)type.GetMethod("TickAsync")!.Invoke(service, new object[] { now.AddHours(1) })!;
        await (Task)type.GetMethod("TickAsync")!.Invoke(service, new object[] { now.AddHours(1) })!;
        Assert.Equal(1, runs);
        service.GetType().GetMethod("Disable")!.Invoke(service, null);
        await (Task)type.GetMethod("TickAsync")!.Invoke(service, new object[] { now.AddDays(2) })!;
        Assert.Equal(1, runs);
    }
}
