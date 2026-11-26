using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Windows;
using WinCareDesktop.Services;
namespace WinCareDesktop.ViewModels;
public partial class DefenderViewModel
{
    private readonly IDefenderActions _actions;
    private DefenderScanTracker? _tracker;
    private Task<RescueCommandResult>? _request;
    private DateTimeOffset _requested;
    private bool _active;
    private CancellationTokenSource? _actionCancel;
    public DefenderViewModel(IDefenderActions actions)
    {
        _actions=actions; Read=actions.Status;
        Title="Defender";Subtitle="Confirmed scans • correlated event observations • no security-policy changes";
    }
    public Action<string,string> Audit {get;set;} = new RescueAudit().Record;
    public Func<RescueScanKind,bool> ConfirmScan {get;set;} = kind=>MessageBox.Show($"Start Microsoft Defender {kind} scan now?\n\nDefender may quarantine or remove threats according to its existing policy. This is not automatically reversible. Full scans can use significant CPU and disk time. No policy changes or hidden elevation will be performed.\n\nStopping monitoring or leaving this page does NOT cancel the scan. Use Windows Security to manage it. Continue?",$"Confirm {kind} scan",MessageBoxButton.YesNo,MessageBoxImage.Warning)==MessageBoxResult.Yes;
    [ObservableProperty] private string _scanStatus="No scan requested. Read protection status to check availability.";
    [ObservableProperty] private string _detections="Detection history not read. No clean verdict is implied.";
    [ObservableProperty] private bool _actionBusy;
    public RescueScanState ScanState=>_tracker?.State ?? RescueScanState.NotStarted;
    public bool CanScan=>_actions.ActionsAllowed && _active && !Busy && !ActionBusy && (_tracker is null || (ScanState is RescueScanState.Completed or RescueScanState.Cancelled && _request?.IsCompleted==true));
    public bool CanMonitor=>_tracker is not null && !ActionBusy;
    private void NotifyActions(){OnPropertyChanged(nameof(CanScan));OnPropertyChanged(nameof(CanMonitor));OnPropertyChanged(nameof(ScanState));}
    partial void OnBusyChanged(bool value)=>NotifyActions();
    partial void OnActionBusyChanged(bool value)=>NotifyActions();
    private static bool Active(DefenderReading data)=>data.Mode=="Normal" && data.Antivirus==true && data.Realtime==true;
    [RelayCommand] public Task QuickScanAsync()=>StartScanAsync(RescueScanKind.Quick);
    [RelayCommand] public Task FullScanAsync()=>StartScanAsync(RescueScanKind.Full);
    public async Task StartScanAsync(RescueScanKind kind)
    {
        if(!CanScan || !Enum.IsDefined(kind)){ScanStatus="Scan blocked: read active protection status first; safe mode, pending requests and unknown outcomes cannot start another scan.";return;}
        ActionBusy=true;using var cts=new CancellationTokenSource(TimeSpan.FromSeconds(35));_actionCancel=cts;
        var launched=false;
        try
        {
            if(!ConfirmScan(kind)){ScanStatus="Scan confirmation declined. No scan requested.";return;}
            cts.Token.ThrowIfCancellationRequested();
            var status=await _actions.Status(cts.Token);cts.Token.ThrowIfCancellationRequested();
            _active=Active(status);
            if(!_active) {ScanStatus="Scan blocked: Defender is not reporting normal active protection.";return;}
            var baseline=await _actions.Events(cts.Token);cts.Token.ThrowIfCancellationRequested();
            if(!baseline.Complete || baseline.Events.Any(e=>e.Id==1000 && !baseline.Events.Any(end=>end.ScanId==e.ScanId && end.Record>e.Record && end.Id is 1001 or 1002)))
            {ScanStatus="Scan blocked: prior scan activity or incomplete event history. Review Windows Security.";return;}
            if(!_actions.ActionsAllowed)throw new InvalidOperationException();
            await Task.Run(()=>Audit(kind.ToString(),"Confirmed start requested"));
            cts.Token.ThrowIfCancellationRequested();
            _requested=DateTimeOffset.UtcNow;
            _tracker=new(kind,baseline.Baseline,_requested,_actions.User);
            ScanStatus=$"{kind}: start requested; not proof of running or completion. Refresh scan lifecycle to read provider events.";
            _request=ObserveRequest(_actions.Start(kind,cts.Token)); launched=true;
            // The provider can block for the duration of a scan. Never block the UI waiting for it.
            await Task.WhenAny(_request,Task.Delay(250,cts.Token));
            if(_request.IsCompleted && (await _request).ExitCode!=0){_tracker.Failed();ScanStatus="Scan request failed or was denied. No elevation was attempted. Scan outcome is not verified; review Windows Security.";Audit(kind.ToString(),"Request failed");}
        }
        catch(OperationCanceledException){ScanStatus=launched?"Monitoring stopped; scan may continue. Refresh lifecycle or review Windows Security.":"Preparation cancelled; no scan requested.";}
        catch(Exception){if(_tracker is not null)_tracker.Unknown();ScanStatus=launched?"Unknown scan outcome or audit failure. Review Windows Security; no retry was started.":"Scan preparation/start failed (provider, permission, policy or audit). No elevation was attempted. Review Windows Security.";}
        finally {_actionCancel=null;ActionBusy=false;NotifyActions();}
    }
    private static async Task<RescueCommandResult> ObserveRequest(Task<RescueCommandResult> task)
    {try{return await task;}catch(Exception){return new(-1,"");}}
    [RelayCommand] public async Task MonitorAsync()
    {
        if(!CanMonitor)return;
        ActionBusy=true;using var cts=new CancellationTokenSource(TimeSpan.FromSeconds(20));_actionCancel=cts;
        try
        {
            var events=await _actions.Events(cts.Token);cts.Token.ThrowIfCancellationRequested();
            _tracker!.Apply(events);
            if(_request?.IsCompleted==true)
            {
                var result=await _request;
                if(result.TimedOut)_tracker.Unknown();else if(result.ExitCode!=0)_tracker.Failed();
            }
            if(ScanState==RescueScanState.StartRequested && DateTimeOffset.UtcNow-_requested>TimeSpan.FromSeconds(30))_tracker.Unknown();
            ScanStatus=$"{ScanState} • observed {DateTimeOffset.Now:T}. "+(ScanState switch {
                RescueScanState.Completed=>"A uniquely correlated scan ID has a completion event. This is not a malware clearance.",
                RescueScanState.Cancelled=>"The correlated scan ID has a cancellation event from Defender. WinCare did not cancel it.",
                RescueScanState.Running=>"A uniquely correlated start event was observed; no matching completion yet. Progress and ETA are unavailable.",
                RescueScanState.Failed=>"The request failed or was denied; no hidden elevation. Review Windows Security.",
                _=>"Completion is unverified. Missing, ambiguous, delayed or unavailable events must be reviewed in Windows Security."})+" Events correlate by ID, user, time and provider; they cannot prove the initiating caller. Refresh manually; no background polling.";
            Audit("Scan lifecycle",ScanState.ToString());
        }
        catch(OperationCanceledException){ScanStatus="Monitoring stopped. This does not cancel Defender; scan may continue. Refresh lifecycle when ready.";}
        catch(Exception){_tracker!.Unknown();ScanStatus="Unknown: event provider or audit unavailable. Scan may continue. Review Windows Security.";}
        finally{_actionCancel=null;ActionBusy=false;NotifyActions();}
    }
    [RelayCommand] public void StopMonitoring()=>_actionCancel?.Cancel();
    [RelayCommand] public async Task ReadDetectionsAsync()
    {
        if(ActionBusy)return;
        ActionBusy=true;using var cts=new CancellationTokenSource(TimeSpan.FromSeconds(20));_actionCancel=cts;
        try{var text=await _actions.Detections(cts.Token);cts.Token.ThrowIfCancellationRequested();Detections=text;}
        catch(OperationCanceledException){Detections="Detection read cancelled; history is incomplete.";}
        catch(Exception){Detections="Detection history unavailable or incomplete (provider, policy or permission). This is not a clean result. Review Protection history.";}
        finally{_actionCancel=null;ActionBusy=false;}
    }
}
