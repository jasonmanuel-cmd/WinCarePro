using System.IO;
using WinCareDesktop.Models;
namespace WinCareDesktop.Services;

public partial class SchedulerService : IDisposable
{
    private readonly Func<CancellationToken, Task<string>>? _runner;
    private ScheduleConfig? _armed;
    private CancellationTokenSource? _cts;
    private bool _running;
    private bool _disposed;
    public DateTime? NextRun { get; private set; }
    public string Status { get; private set; } = "Disabled. Fresh confirmation required each app session.";
    public SchedulerService(string path, Func<CancellationToken, Task<string>> runner) : this(path) => _runner = runner;

    public void EnableForSession(ScheduleConfig config, DateTime now)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_running) throw new InvalidOperationException("Wait for the previous cleanup to stop before enabling again.");
        if (_runner == null) SafetyMode.RequireMutationAllowed();
        if (config.Frequency is not ("Daily" or "Weekly") || !TimeSpan.TryParseExact(config.TimeOfDay, @"hh\:mm", null, out var time) || time.TotalHours >= 24 || !config.CleanTempFiles)
            throw new ArgumentException("Choose Daily or Weekly, HH:mm local time, and temporary-file cleanup.");
        Disable();
        var armed = new ScheduleConfig { IsEnabled = true, Frequency = config.Frequency, TimeOfDay = config.TimeOfDay, CleanTempFiles = true, EmptyRecycleBin = false, OptimizeRam = false, FlushDns = false, ConsentVersion = 1 };
        SaveConfig(armed);
        _armed = armed;
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        NextRun = now.Date + time;
        if (NextRun <= now) NextRun = NextRun.Value.AddDays(config.Frequency == "Weekly" ? 7 : 1);
        Status = $"App-open only. Next run: {NextRun:g}.";
    }

    public void Disable() { if (_armed != null) _armed.IsEnabled = false; _armed = null; NextRun = null; if (!_disposed) _cts?.Cancel(); Status = "Disabled; running work stops before the next file."; }
    public async Task TickAsync(DateTime now)
    {
        if (_running || _armed == null || NextRun == null || now < NextRun) return;
        var config = _armed;
        NextRun = (now.Date + TimeSpan.Parse(config.TimeOfDay)).AddDays(config.Frequency == "Weekly" ? 7 : 1);
        _running = true;
        try
        {
            config.LastRun = now;
            config.LastResult = "Attempt started";
            SaveConfig(config);
            config.LastResult = await (_runner ?? RunBoundedAsync)(_cts!.Token);
            if (_armed == config) Status = config.LastResult;
        }
        catch (OperationCanceledException) { config.LastResult = "Cleanup cancelled; completed files are not restored."; if (_armed == config) Status = config.LastResult; }
        catch (Exception ex) { config.LastResult = "Cleanup incomplete: " + ex.Message; if (_armed == config) Status = config.LastResult; }
        finally
        {
            _running = false;
            try { SaveConfig(config); } catch (Exception ex) { Disable(); Status = "Scheduler disabled: cannot save result. " + ex.Message; }
            if (_disposed) _cts?.Dispose();
        }
    }

    private static Task<string> RunBoundedAsync(CancellationToken token) => Task.Run(() =>
    {
        SafetyMode.RequireMutationAllowed();
        var root = Path.GetFullPath(Path.GetTempPath());
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var service = new SystemOptimizerService { CleanupRoots = new[] { root } };
        long bytes = 0; int attempted = 0, errors = 0, inspected = 0;
        foreach (var path in Directory.EnumerateFiles(root))
        {
            token.ThrowIfCancellationRequested();
            if (++inspected > 1000 || attempted >= 100 || watch.Elapsed > TimeSpan.FromSeconds(15)) break;
            try
            {
                PathSafety.RejectReparsePoints(path);
                var info = new FileInfo(path);
                if (info.LastWriteTimeUtc > DateTime.UtcNow.AddDays(-7) || info.Length > 104857600 - bytes) continue;
                var candidate = new CleanupCandidate { Path = path, Bytes = info.Length, LastWriteUtc = info.LastWriteTimeUtc, CreatedUtc = info.CreationTimeUtc };
                bytes += service.CleanTempAndCache(new[] { candidate });
                errors += service.LastCleanupErrors;
                attempted++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { errors++; }
        }
        return $"Auto-clean: {bytes} bytes deleted; {errors} errors; {attempted} files attempted.";
    }, token);
    public void Dispose() { if (_disposed) return; Disable(); _disposed = true; if (!_running) _cts?.Dispose(); }
}
