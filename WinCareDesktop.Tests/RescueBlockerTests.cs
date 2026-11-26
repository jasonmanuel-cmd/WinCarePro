using WinCareDesktop.Services;
using Xunit;
namespace WinCareDesktop.Tests;

public class RescueBlockerTests
{
    [Fact] public async Task Cancellation_during_audit_never_requests_scan()
    {
        var fake=new DefenderActionTests.Fake();var vm=DefenderActionTests.Vm(fake);
        await vm.RefreshAsync();vm.Audit=(_,_)=>vm.StopMonitoring();
        await vm.QuickScanAsync();
        Assert.Equal(0,fake.Starts);
        Assert.Contains("no scan requested",vm.ScanStatus);
    }

    [Fact] public void Sequential_watermark_regression_above_initial_baseline_latches_unknown()
    {
        var now=DateTimeOffset.UtcNow;
        var start=new DefenderScanEvent(90,now.AddSeconds(1),1000,"11111111-1111-1111-1111-111111111111","Quick Scan","fixture","Microsoft-Windows-Windows Defender");
        var tracker=new DefenderScanTracker(RescueScanKind.Quick,10,now,"fixture");
        tracker.Apply(new(100,new[]{start},true));Assert.Equal(RescueScanState.Running,tracker.State);
        tracker.Apply(new(50,Array.Empty<DefenderScanEvent>(),true));Assert.Equal(RescueScanState.Unknown,tracker.State);
        tracker.Apply(new(110,new[]{start,start with {Record=105,Id=1001}},true));Assert.Equal(RescueScanState.Unknown,tracker.State);
    }

    [Fact] public void Missing_previously_observed_start_latches_unknown()
    {
        var now=DateTimeOffset.UtcNow;
        var start=new DefenderScanEvent(90,now.AddSeconds(1),1000,Guid.NewGuid().ToString(),"Quick Scan","fixture","Microsoft-Windows-Windows Defender");
        var tracker=new DefenderScanTracker(RescueScanKind.Quick,10,now,"fixture");
        tracker.Apply(new(100,new[]{start},true));
        tracker.Apply(new(110,Array.Empty<DefenderScanEvent>(),true));
        Assert.Equal(RescueScanState.Unknown,tracker.State);
        tracker.Apply(new(120,new[]{start,start with {Record=115,Id=1001}},true));
        Assert.Equal(RescueScanState.Unknown,tracker.State);
    }

    [Fact] public void Events_above_final_watermark_cannot_establish_completion()
    {
        var now=DateTimeOffset.UtcNow;
        var start=new DefenderScanEvent(90,now.AddSeconds(1),1000,Guid.NewGuid().ToString(),"Quick Scan","fixture","Microsoft-Windows-Windows Defender");
        var tracker=new DefenderScanTracker(RescueScanKind.Quick,10,now,"fixture");
        tracker.Apply(new(50,new[]{start,start with {Record=91,Id=1001}},true));
        Assert.Equal(RescueScanState.Unknown,tracker.State);
    }

    [Theory][InlineData(100,101,true)][InlineData(100,50,false)]
    public async Task Native_snapshot_contract_checks_final_watermark_using_only_fixture_events(long before,long after,bool complete)
    {
        var native=new NativeDefenderActions((script,mutation,timeout,token)=>
        {
            Assert.False(mutation);
            Assert.Contains(@"Microsoft.PowerShell.Diagnostics\Get-WinEvent",script);
            // Replace ALL event queries with an inert fixture; no host event log is read.
            var fixture="$script:reads=0; function FixtureEvent { param($LogName,$MaxEvents,$ErrorAction,$FilterXPath) $script:reads++; if($MaxEvents -eq 1){if($script:reads -eq 1){[pscustomobject]@{RecordId="+before+"}}else{[pscustomobject]@{RecordId="+after+"}}} }; ";
            script=script.Replace("Microsoft.PowerShell.Diagnostics\\Get-WinEvent","FixtureEvent");
            return RescuePowerShell.RunAsync(fixture+script,false,timeout,token);
        });
        var batch=await native.Events(default);
        Assert.Equal(after,batch.Baseline);Assert.Equal(complete,batch.Complete);Assert.Empty(batch.Events);
    }

    [Fact] public void Cancellation_at_native_launch_boundary_never_calls_process_factory()
    {
        using var cts=new CancellationTokenSource();
        var info=RescuePowerShellPolicy.CreateStartInfo("[Console]::WriteLine('fixture')");
        cts.Cancel();var calls=0;
        Assert.Throws<OperationCanceledException>(()=>RescuePowerShell.Launch(info,false,cts.Token,_=>{calls++;return null;}));
        Assert.Equal(0,calls);
    }

    [Fact] public async Task Cancelled_native_start_never_reaches_runner()
    {
        var calls=0;var native=new NativeDefenderActions((_,_,_,_)=>{calls++;throw new Exception("Must not run");});
        await Assert.ThrowsAsync<OperationCanceledException>(()=>native.Start(RescueScanKind.Quick,new CancellationToken(true)));
        Assert.Equal(0,calls);
    }

    [Fact] public void PowerShell_environment_and_bootstrap_pin_all_provider_dependencies()
    {
        var info=RescuePowerShellPolicy.CreateStartInfo("[Console]::WriteLine('fixture')");
        var system=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"System32");
        Assert.Equal(Path.Combine(system,"WindowsPowerShell","v1.0","Modules"),info.Environment["PSModulePath"]);
        Assert.Equal("NUL",info.Environment["PSModuleAnalysisCachePath"]);
        Assert.DoesNotContain("USERPROFILE",info.Environment.Keys);
        var script=info.ArgumentList.Last();
        Assert.Contains("$PSModuleAutoLoadingPreference='None'",script);
        foreach(var command in new[]{"Defender\\Start-MpScan","Defender\\Get-MpThreatDetection","Microsoft.PowerShell.Security\\Get-AuthenticodeSignature","Microsoft.PowerShell.Diagnostics\\Get-WinEvent"})Assert.Contains(command,script);
        Assert.Contains("Unexpected provider command origin",script);
    }

    [Fact] public async Task Trusted_command_discovery_includes_detection_and_signature_dependencies()
    {
        // Imports/discovery only. No Defender scan, detection query or application-close action.
        var result=await RescuePowerShell.RunAsync("foreach($name in @('Defender\\Start-MpScan','Defender\\Get-MpThreatDetection','Microsoft.PowerShell.Security\\Get-AuthenticodeSignature','Microsoft.PowerShell.Diagnostics\\Get-WinEvent')){$c=Microsoft.PowerShell.Core\\Get-Command $name -ErrorAction Stop; [Console]::WriteLine($c.Module.Path)}",false,TimeSpan.FromSeconds(30),default);
        Assert.Equal(0,result.ExitCode);Assert.False(result.TimedOut);
        var paths=result.Output.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(4,paths.Length);
        foreach(var path in paths)Assert.StartsWith(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"System32","WindowsPowerShell","v1.0","Modules"),path,StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Defender","Start-MpScan")]
    [InlineData("Defender","Get-MpThreatDetection")]
    [InlineData("Microsoft.PowerShell.Security","Get-AuthenticodeSignature")]
    public async Task Shadow_module_cannot_supply_Defender_commands(string moduleName,string command)
    {
        // Discovery only. The fixture exports throwing functions; NO scan/detection cmdlet is invoked.
        var root=Path.Combine(Path.GetTempPath(),"wincare-shadow-"+Guid.NewGuid());
        var module=Path.Combine(root,moduleName);Directory.CreateDirectory(module);
        await File.WriteAllTextAsync(Path.Combine(module,moduleName+".psm1"),"function "+command+" { throw 'NEVER invoke fixture provider' }; Export-ModuleMember -Function "+command);
        var old=Environment.GetEnvironmentVariable("PSModulePath");
        var cache=Environment.GetEnvironmentVariable("PSModuleAnalysisCachePath");
        try
        {
            Environment.SetEnvironmentVariable("PSModulePath",root);
            Environment.SetEnvironmentVariable("PSModuleAnalysisCachePath",Path.Combine(root,"cache"));
            var result=await RescuePowerShell.RunAsync("$c=Microsoft.PowerShell.Core\\Get-Command "+command+" -ErrorAction Stop; [Console]::WriteLine($c.Module.Path)",false,TimeSpan.FromSeconds(30),default);
            Assert.False(result.TimedOut);
            Assert.DoesNotContain(root,result.Output,StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0,result.ExitCode); // This host has the inbox module; prove useful trusted discovery, not blanket denial.
            Assert.Contains(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"System32","WindowsPowerShell","v1.0","Modules",moduleName),result.Output,StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PSModulePath",old);
            Environment.SetEnvironmentVariable("PSModuleAnalysisCachePath",cache);
            Directory.Delete(root,true);
        }
    }
}
