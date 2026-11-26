using System.Diagnostics;
namespace WinCareDesktop.Services;
public sealed record MemoryReading(DateTimeOffset At,long TotalBytes,long UsedBytes,IReadOnlyList<MemoryRow> Rows,int Observed,int Skipped);
public static class NativeMemoryReader
{
    // Read only. No process handle is retained, no ownership/close eligibility is inferred.
    public static async Task<MemoryReading> ReadAsync(CancellationToken token)
    {
        var before=await Task.Run(()=>Capture(token),token);
        var clock=Stopwatch.StartNew();
        await Task.Delay(500,token);
        var after=await Task.Run(()=>Capture(token),token);
        var elapsed=clock.Elapsed;
        token.ThrowIfCancellationRequested();
        var stats=await Task.Run(()=>new SystemOptimizerService().GetRamAndStorageStats(),token);
        token.ThrowIfCancellationRequested();
        return new(DateTimeOffset.UtcNow,stats.totalBytes,stats.totalBytes-stats.availableBytes,
            MemorySampler.Compare(before.Rows,after.Rows,elapsed,Environment.ProcessorCount),after.Rows.Count,after.Skipped);
    }
    private static (List<ProcessSample> Rows,int Skipped) Capture(CancellationToken token)
    {
        var rows=new List<ProcessSample>(); var skipped=0;
        var processes=Process.GetProcesses();
        try
        {
            // Fixed work bound. Omitted/system/inaccessible processes are not considered unnecessary.
            foreach(var p in processes.Take(512))
            {
                token.ThrowIfCancellationRequested();
                try { rows.Add(new(p.Id,p.StartTime.ToUniversalTime().Ticks,p.ProcessName,p.WorkingSet64,p.TotalProcessorTime.TotalMilliseconds,Stopwatch.GetTimestamp())); }
                catch(InvalidOperationException) { skipped++; }
                catch(System.ComponentModel.Win32Exception) { skipped++; }
            }
            skipped+=Math.Max(0,processes.Length-512);
        }
        finally {foreach(var p in processes)p.Dispose();}
        return (rows,skipped);
    }
}
