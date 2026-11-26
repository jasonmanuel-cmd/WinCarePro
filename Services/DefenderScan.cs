using System.Diagnostics;
using System.IO;
using System.Text.Json;
namespace WinCareDesktop.Services;

public enum RescueScanKind { Quick, Full }
public enum RescueScanState { NotStarted, StartRequested, Running, Completed, Cancelled, Failed, Unknown }
public sealed record DefenderScanEvent(long Record, DateTimeOffset At, int Id, string ScanId, string Parameters, string User, string Provider);
public sealed record DefenderEventBatch(long Baseline, IReadOnlyList<DefenderScanEvent> Events, bool Complete);
public sealed record RescueCommandResult(int ExitCode, string Output, bool TimedOut = false);
public interface IDefenderActions
{
    bool ActionsAllowed { get; }
    string User { get; }
    Task<DefenderReading> Status(CancellationToken token);
    Task<DefenderEventBatch> Events(CancellationToken token);
    Task<RescueCommandResult> Start(RescueScanKind kind, CancellationToken preparationToken = default);
    Task<string> Detections(CancellationToken token);
}

/// <summary>Events establish lifecycle, not proof of which caller requested a scan.</summary>
public sealed class DefenderScanTracker(RescueScanKind kind, long baseline, DateTimeOffset requested, string user)
{
    public RescueScanState State { get; private set; } = RescueScanState.StartRequested;
    public string? ScanId { get; private set; }
    private long _startRecord;
    private DateTimeOffset _started;
    private bool _ambiguous;
    private readonly long _baseline=baseline;
    private long _highWatermark=baseline;
    public void Apply(DefenderEventBatch batch)
    {
        if(_ambiguous)return;
        // Native batches advertise the watermark AFTER enumeration. Lost history is irreversible.
        if (!batch.Complete || batch.Baseline < _highWatermark || batch.Events.Any(e=>e.Record>batch.Baseline) ||
            (ScanId is not null && !batch.Events.Any(e=>e.Record==_startRecord && e.ScanId==ScanId && e.Id==1000)))
        { Unknown(); return; }
        _highWatermark=batch.Baseline;
        var events=batch.Events.Where(e=>e.Provider=="Microsoft-Windows-Windows Defender" && e.Record>_baseline && e.At>=requested).OrderBy(e=>e.Record).ToArray();
        // More than one candidate start in the bounded initiation window is ambiguous.
        var starts=events.Where(e=>e.Id==1000 && e.At<=requested.AddSeconds(30)).ToArray();
        if(starts.Length>1) {_ambiguous=true;State=RescueScanState.Unknown;ScanId=null;return;}
        if(ScanId is null)
        {
            var start=starts.SingleOrDefault();
            if(start is null)return;
            if(!Guid.TryParse(start.ScanId,out _) || !string.Equals(start.User,user,StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(start.Parameters,kind==RescueScanKind.Quick?"Quick Scan":"Full Scan",StringComparison.OrdinalIgnoreCase))
            {State=RescueScanState.Unknown;return;}
            ScanId=start.ScanId; _startRecord=start.Record; _started=start.At; State=RescueScanState.Running;
        }
        var end=events.FirstOrDefault(e=>e.ScanId==ScanId && e.Record>_startRecord && e.At>=_started && e.Id is 1001 or 1002);
        if(end is not null)State=end.Id==1001?RescueScanState.Completed:RescueScanState.Cancelled;
    }
    public void Unknown(){_ambiguous=true;State=RescueScanState.Unknown;}
    public void Failed()=>State=RescueScanState.Failed;
}

/// <summary>Only fixed scripts enter this adapter. No shell elevation or policy changes.</summary>
public sealed class NativeDefenderActions : IDefenderActions
{
    private readonly Func<string,bool,TimeSpan,CancellationToken,Task<RescueCommandResult>> _run;
    public NativeDefenderActions(Func<string,bool,TimeSpan,CancellationToken,Task<RescueCommandResult>>? run=null) => _run=run ?? RescuePowerShell.RunAsync;
    public bool ActionsAllowed=>!SafetyMode.Enabled;
    public string User=>System.Security.Principal.WindowsIdentity.GetCurrent().Name;
    public Task<DefenderReading> Status(CancellationToken token)=>DefenderStatusReader.ReadAsync(token);
    public async Task<DefenderEventBatch> Events(CancellationToken token)
    {
        var result=await _run("$log='Microsoft-Windows-Windows Defender/Operational'; $last=Microsoft.PowerShell.Diagnostics\\Get-WinEvent -LogName $log -MaxEvents 1 -ErrorAction Stop; $items=@(Microsoft.PowerShell.Diagnostics\\Get-WinEvent -LogName $log -FilterXPath '*[System[(EventID=1000 or EventID=1001 or EventID=1002)]]' -MaxEvents 257 -ErrorAction SilentlyContinue); if($Error.Count -gt 0 -and $Error[0].FullyQualifiedErrorId -notlike 'NoMatchingEventsFound*'){throw 'Event read failed'}; $after=Microsoft.PowerShell.Diagnostics\\Get-WinEvent -LogName $log -MaxEvents 1 -ErrorAction Stop; $consistent=($after.RecordId -ge $last.RecordId); foreach($event in $items){if($event.RecordId -gt $after.RecordId){$consistent=$false}}; $rows=@($items | Microsoft.PowerShell.Core\\ForEach-Object { $x=[xml]$_.ToXml(); $d=@{}; foreach($v in $x.Event.EventData.Data){$d[$v.Name]=$v.'#text'}; [pscustomobject]@{Record=$_.RecordId;At=$_.TimeCreated.ToUniversalTime().ToString('o');Id=$_.Id;ScanId=$d['Scan ID'];Parameters=$d['Scan Parameters'];User=$d['User'];Provider=$_.ProviderName} }); [pscustomobject]@{Baseline=$after.RecordId;Events=$rows;Complete=($items.Count -lt 257 -and $consistent)} | Microsoft.PowerShell.Utility\\ConvertTo-Json -Depth 5 -Compress",false,TimeSpan.FromSeconds(15),token);
        if(result.ExitCode!=0 || result.TimedOut)throw new IOException("Defender event provider unavailable.");
        return JsonSerializer.Deserialize<DefenderEventBatch>(result.Output) ?? throw new IOException("Invalid event response.");
    }
    public Task<RescueCommandResult> Start(RescueScanKind kind, CancellationToken preparationToken = default)
    {
        preparationToken.ThrowIfCancellationRequested();
        SafetyMode.RequireMutationAllowed();
        if(!Enum.IsDefined(kind))throw new ArgumentOutOfRangeException(nameof(kind));
        return _run("Defender\\Start-MpScan -ScanType "+(kind==RescueScanKind.Quick?"QuickScan":"FullScan")+" -ErrorAction Stop",true,TimeSpan.FromHours(8),preparationToken);
    }
    public async Task<string> Detections(CancellationToken token)
    {
        var result=await _run("$d=@(Defender\\Get-MpThreatDetection -ErrorAction Stop | Microsoft.PowerShell.Utility\\Select-Object -First 101); [pscustomobject]@{Count=$d.Count;Rows=@($d | Microsoft.PowerShell.Utility\\Select-Object -First 100 ThreatID,ActionSuccess,ThreatStatusID)} | Microsoft.PowerShell.Utility\\ConvertTo-Json -Depth 4 -Compress",false,TimeSpan.FromSeconds(15),token);
        if(result.ExitCode!=0 || result.TimedOut)throw new IOException("Detection history unavailable.");
        using var json=JsonDocument.Parse(result.Output);var root=json.RootElement;
        var rows=root.GetProperty("Rows").EnumerateArray().Select(r=>$"Threat ID {r.GetProperty("ThreatID")} • provider status {r.GetProperty("ThreatStatusID")} • action success {r.GetProperty("ActionSuccess")}");
        return "Active and past detections (not tied to this scan). "+(root.GetProperty("Count").GetInt32()>100?"Incomplete: first 100 only. ":"")+string.Join("\n",rows)+"\nAn empty list is not a clean verdict. Review Protection history for names, affected items and supported remediation.";
    }
}

public static class RescuePowerShell
{
    public static async Task<RescueCommandResult> RunAsync(string script,bool mutation,TimeSpan timeout,CancellationToken token)
    {
        if(mutation)SafetyMode.RequireMutationAllowed();
        token.ThrowIfCancellationRequested();
        var info=RescuePowerShellPolicy.CreateStartInfo(script);
        using var process=Launch(info,mutation,token);
        // Preparation cancellation is consumed at launch, not a scan-cancel mechanism.
        var observationToken=mutation?CancellationToken.None:token;
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(observationToken);deadline.CancelAfter(timeout);
        var output=ReadBounded(process.StandardOutput);var errors=ReadBounded(process.StandardError);
        try {await process.WaitForExitAsync(deadline.Token);return new(process.ExitCode,await output);}
        catch(OperationCanceledException)
        {
            // Stop only our wrapper, never Defender or a process tree. Scan outcome remains unknown.
            if(!process.HasExited)process.Kill();
            await process.WaitForExitAsync();
            if(observationToken.IsCancellationRequested)throw;
            return new(-1,"",true);
        }
        finally {await output;await errors;}
    }
    internal static Process Launch(ProcessStartInfo info,bool mutation,CancellationToken preparationToken,Func<ProcessStartInfo,Process?>? start=null)
    {
        if(mutation)SafetyMode.RequireMutationAllowed();
        preparationToken.ThrowIfCancellationRequested();
        return (start ?? Process.Start)(info) ?? throw new IOException("Provider process did not start.");
    }
    private static async Task<string> ReadBounded(StreamReader reader)
    {
        var text=new System.Text.StringBuilder();var buffer=new char[4096];int count;
        while((count=await reader.ReadAsync(buffer))>0)if(text.Length<131072)text.Append(buffer,0,Math.Min(count,131072-text.Length));
        return text.ToString();
    }
}
