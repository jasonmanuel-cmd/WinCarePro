using WinCareDesktop.Services;
using WinCareDesktop.ViewModels;
using Xunit;
namespace WinCareDesktop.Tests;
public class DefenderActionTests
{
    private static readonly DateTimeOffset Now=DateTimeOffset.UtcNow;
    private const string Id="11111111-1111-1111-1111-111111111111";
    private static DefenderScanEvent E(int id=1000,long record=11,string scan=Id)=>new(record,Now.AddSeconds(1),id,scan,"Quick Scan","fixture-user","Microsoft-Windows-Windows Defender");
    [Fact] public void Completion_requires_matching_started_scan_id()
    {
        var t=new DefenderScanTracker(RescueScanKind.Quick,10,Now,"fixture-user");
        t.Apply(new(20,new[]{E(1001)},true));Assert.Equal(RescueScanState.StartRequested,t.State);
        t.Apply(new(20,new[]{E(),E(1001,12,Guid.NewGuid().ToString())},true));Assert.Equal(RescueScanState.Running,t.State);
        t.Apply(new(20,new[]{E(),E(1001,13)},true));Assert.Equal(RescueScanState.Completed,t.State);
    }
    [Theory][InlineData(1001,RescueScanState.Completed)][InlineData(1002,RescueScanState.Cancelled)]
    public void Lifecycle_uses_provider_event_not_process_exit(int end,RescueScanState expected)
    {var t=new DefenderScanTracker(RescueScanKind.Quick,10,Now,"fixture-user");t.Apply(new(30,new[]{E(),E(end,12)},true));Assert.Equal(expected,t.State);}
    [Theory][InlineData("provider")][InlineData("time")][InlineData("baseline")][InlineData("user")][InlineData("kind")]
    public void Other_scans_are_not_claimed(string changed)
    {
        var e=E();e=changed switch {"provider"=>e with{Provider="other"},"time"=>e with{At=Now.AddMinutes(-1)},"baseline"=>e with{Record=9},"user"=>e with{User="other"},_=>e with{Parameters="Full Scan"}};
        var t=new DefenderScanTracker(RescueScanKind.Quick,10,Now,"fixture-user");t.Apply(new(20,new[]{e,E(1001,12)},true));Assert.NotEqual(RescueScanState.Completed,t.State);Assert.NotEqual(RescueScanState.Running,t.State);
    }
    [Fact] public void Ambiguous_start_is_permanently_unknown_even_if_later_snapshot_hides_competitor()
    {
        var t=new DefenderScanTracker(RescueScanKind.Quick,10,Now,"fixture-user");
        t.Apply(new(30,new[]{E(),E(1000,12,Guid.NewGuid().ToString())},true));Assert.Equal(RescueScanState.Unknown,t.State);
        t.Apply(new(30,new[]{E(),E(1001,13)},true));Assert.Equal(RescueScanState.Unknown,t.State);
    }
    [Fact] public async Task Confirmed_quick_start_invokes_only_fake_and_successful_exit_is_not_completion()
    {
        var fake=new Fake();var vm=Vm(fake);await vm.RefreshAsync();await vm.QuickScanAsync();
        Assert.Equal(1,fake.Starts);Assert.Equal(RescueScanKind.Quick,fake.Kind);Assert.Equal(RescueScanState.StartRequested,vm.ScanState);Assert.False(vm.CanScan);
        await vm.FullScanAsync();Assert.Equal(1,fake.Starts);
    }
    [Fact] public async Task Declined_confirmation_does_not_request_scan()
    {var f=new Fake();var vm=Vm(f);await vm.RefreshAsync();vm.ConfirmScan=_=>false;await vm.FullScanAsync();Assert.Equal(0,f.Starts);}
    [Fact] public async Task Availability_is_rechecked_after_confirmation()
    {var f=new Fake();var vm=Vm(f);await vm.RefreshAsync();vm.ConfirmScan=_=>{f.Active=false;return true;};await vm.FullScanAsync();Assert.Equal(0,f.Starts);}
    [Fact] public async Task Audit_failure_blocks_launch()
    {var f=new Fake();var vm=Vm(f);await vm.RefreshAsync();vm.Audit=(_,_)=>throw new IOException();await vm.QuickScanAsync();Assert.Equal(0,f.Starts);}
    [Fact] public async Task Denial_is_failed_not_elevated_or_complete()
    {var f=new Fake{Result=new(1,"")};var vm=Vm(f);await vm.RefreshAsync();await vm.FullScanAsync();Assert.Equal(RescueScanState.Failed,vm.ScanState);Assert.Contains("No elevation",vm.ScanStatus);}
    [Fact] public async Task Safe_native_adapter_and_root_refuse_before_start()
    {
        var vm=new DefenderViewModel();vm.Read=_=>Task.FromResult(new DefenderReading("Normal",true,true,null,"fixture"));vm.ConfirmScan=_=>throw new Exception("Must not confirm");await vm.RefreshAsync();await vm.QuickScanAsync();Assert.False(vm.CanScan);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>new NativeDefenderActions().Start(RescueScanKind.Quick));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>RescuePowerShell.RunAsync("throw 'Never executed'",true,TimeSpan.FromSeconds(1),default));
    }
    [Fact] public async Task Stopping_monitoring_discards_late_events_without_cancelling_request()
    {
        var f=new Fake();var vm=Vm(f);await vm.RefreshAsync();await vm.QuickScanAsync();
        var pending=new TaskCompletionSource<DefenderEventBatch>();f.EventRead=_=>pending.Task;
        var task=vm.MonitorAsync();vm.OnNavigatedFrom();pending.SetResult(new(20,Array.Empty<DefenderScanEvent>(),true));await task;
        Assert.Contains("does not cancel",vm.ScanStatus);Assert.Equal(1,f.Starts);Assert.False(vm.CanScan);
    }
    [Fact] public async Task Unmatched_prior_start_blocks_another_scan()
    {
        var f=new Fake{Batch=new(20,new[]{E()},true)};var vm=Vm(f);await vm.RefreshAsync();await vm.QuickScanAsync();Assert.Equal(0,f.Starts);
    }
    [Fact] public async Task Incomplete_baseline_blocks_start()
    {
        var f=new Fake{Batch=new(20,Array.Empty<DefenderScanEvent>(),false)};var vm=Vm(f);await vm.RefreshAsync();await vm.FullScanAsync();Assert.Equal(0,f.Starts);
    }
    [Fact] public async Task Detection_failure_is_visible_and_not_clean()
    {
        var f=new Fake{DetectionRead=_=>throw new IOException("private path")};var vm=Vm(f);await vm.ReadDetectionsAsync();Assert.Contains("incomplete",vm.Detections);Assert.DoesNotContain("private path",vm.Detections);
    }
    [Fact] public void Native_defender_supports_injected_bounded_process_runner()
    {Assert.Contains(typeof(NativeDefenderActions).GetConstructors(),c=>c.GetParameters().Length==1);}
    [Fact] public async Task Native_event_reader_uses_bounded_read_only_runner_and_parses_provider_batch()
    {
        var calls=0;var native=new NativeDefenderActions((script,mutation,timeout,token)=>{
            calls++;Assert.False(mutation);Assert.Equal(TimeSpan.FromSeconds(15),timeout);Assert.Contains("Get-WinEvent",script);
            return Task.FromResult(new RescueCommandResult(0,"{\"Baseline\":42,\"Events\":[],\"Complete\":true}"));});
        var batch=await native.Events(default);Assert.Equal(42,batch.Baseline);Assert.True(batch.Complete);Assert.Equal(1,calls);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>native.Start(RescueScanKind.Full));Assert.Equal(1,calls);
    }
    [Fact] public async Task Native_detection_reader_preserves_incomplete_state_without_resource_paths()
    {
        var native=new NativeDefenderActions((script,mutation,timeout,token)=>Task.FromResult(new RescueCommandResult(0,"{\"Count\":101,\"Rows\":[{\"ThreatID\":123,\"ActionSuccess\":false,\"ThreatStatusID\":1}]}")));
        var text=await native.Detections(default);Assert.Contains("Incomplete",text);Assert.Contains("123",text);Assert.Contains("not tied",text);
    }
    internal static DefenderViewModel Vm(Fake f)=>new(f){ConfirmScan=_=>true,Audit=(_,_)=>{}};
    internal sealed class Fake:IDefenderActions
    {
        public bool ActionsAllowed=>true;public string User=>"fixture-user";public bool Active=true;public int Starts;public RescueScanKind Kind;public RescueCommandResult Result=new(0,"");
        public DefenderEventBatch Batch=new(10,Array.Empty<DefenderScanEvent>(),true);
        public Task<DefenderReading> Status(CancellationToken token)=>Task.FromResult(new DefenderReading(Active?"Normal":"Passive",true,true,null,"fixture"));
        public Func<CancellationToken,Task<DefenderEventBatch>>? EventRead;
        public Func<CancellationToken,Task<string>>? DetectionRead;
        public Task<DefenderEventBatch> Events(CancellationToken token)=>EventRead?.Invoke(token) ?? Task.FromResult(Batch);
        public Task<RescueCommandResult> Start(RescueScanKind kind,CancellationToken preparationToken=default){preparationToken.ThrowIfCancellationRequested();Starts++;Kind=kind;return Task.FromResult(Result);}
        public Task<string> Detections(CancellationToken token)=>DetectionRead?.Invoke(token) ?? Task.FromResult("Synthetic detection history");
    }
}
