using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using WinCareDesktop.Models;
using WinCareDesktop.Services;
using System.IO;
using System.Windows;

namespace WinCareDesktop.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly SystemOptimizerService _svc = new();

    /// <summary>
    /// The one service instance, exposed so the window's tool panels share the
    /// same undo-history store rather than opening a second one.
    /// </summary>
    public SystemOptimizerService Service => _svc;

    [ObservableProperty] private double _score;
    [ObservableProperty] private double _ramPercent;
    [ObservableProperty] private double _storagePercent;
    [ObservableProperty] private double _batteryPercent = -1;
    [ObservableProperty] private double _reclaimedGb;
    [ObservableProperty] private int _trackersBlocked;
    [ObservableProperty] private string _activeDns = "System default";
    [ObservableProperty] private bool _isOptimizing;
    [ObservableProperty] private string _statusMessage = "System ready.";
    [ObservableProperty] private double _totalRamGb;
    [ObservableProperty] private double _freeStorageGb;

    [ObservableProperty] private double _cpuPercent = -1;
    [ObservableProperty] private string _uptimeText = "—";
    [ObservableProperty] private string _diskHealthText = "Checking…";
    [ObservableProperty] private string _topMemoryText = "—";

    public ObservableCollection<string> Logs { get; } = new();
    public ObservableCollection<BloatwareApp> BloatwareApps { get; } = new();
    public ObservableCollection<DnsResolver> DnsResolvers { get; } = new();
    public ObservableCollection<OptimizationTransaction> UndoHistory { get; } = new();
    public ObservableCollection<PrivacyShield> Shields { get; } = new();
    public ObservableCollection<CleanupCandidate> CleanupPreview { get; } = new();
    public ObservableCollection<ProcessInfo> TopProcesses { get; } = new();

    public event Action? SystemStatsChanged;
    public event Action<double>? HealthScoreChanged;
    public event Action<string>? LogAdded;

    private readonly List<DnsResolver> _dnsList = new()
    {
        new() { Id = "cloudflare", Name = "Cloudflare", Ip = "1.1.1.1", Ping = "", Active = false },
        new() { Id = "google",     Name = "Google", Ip = "8.8.8.8", Ping = "", Active = false },
        new() { Id = "quad9",      Name = "Quad9", Ip = "9.9.9.9", Ping = "", Active = false },
    };

    public MainViewModel()
    {
        foreach (var dns in _dnsList) DnsResolvers.Add(dns);
        LoadPrivacyFromSystem();
        SyncDnsFromSystem();
        // Restore persisted history before the first RefreshStats so the
        // Undo History screen is populated on launch.
        _svc.LoadUndoHistory();
        RefreshStats();
        // Never called before, so the Undo History screen always reported
        // "No actions to undo yet" even after a maintenance run.
        LoadUndoHistory();
        Log("WinCare Pro initialized. System telemetry active.", "system");
    }

    public void Log(string msg, string type = "system")
    {
        var entry = $"[{DateTime.Now:HH:mm:ss}] [{type.ToUpper()}] {msg}";
        var dispatcher = App.Current?.Dispatcher;
        if (dispatcher == null)
        {
            AppendLog(entry);
        }
        else if (dispatcher.CheckAccess())
        {
            AppendLog(entry);
        }
        else
        {
            dispatcher.Invoke(() => AppendLog(entry));
        }
        LogAdded?.Invoke(msg);
    }

    private void AppendLog(string entry)
    {
        Logs.Add(entry);
        while (Logs.Count > 300) Logs.RemoveAt(0);
    }

    public void TogglePrivacyShield(string shieldName, bool enabled)
    {
        if (_svc.ApplyPrivacySetting(shieldName, enabled))
            TrackersBlocked = CountEnabledShields();
        RefreshStats();
    }

    private void LoadPrivacyFromSystem()
    {
        var adOff = _svc.ReadAdvertisingIdBlocked();
        var tailoredOff = _svc.ReadTailoredExperiencesOff();
        Shields.Add(new PrivacyShield { Name = "Advertising ID", Enabled = adOff });
        Shields.Add(new PrivacyShield { Name = "User Telemetry", Enabled = tailoredOff });
        Shields.Add(new PrivacyShield { Name = "Diagnostic Data", Enabled = tailoredOff });
        Shields.Add(new PrivacyShield { Name = "Background Tracking", Enabled = adOff });
        TrackersBlocked = CountEnabledShields();
    }

    private int CountEnabledShields() => Shields.Count(s => s.Enabled);

    public void SyncDnsFromSystem()
    {
        var match = _svc.MatchCurrentDns(_dnsList.Select(d => (d.Name, d.Ip)));
        ActiveDns = match.name;
        foreach (var d in _dnsList)
            d.Active = match.matched && string.Equals(d.Ip, match.ip, StringComparison.OrdinalIgnoreCase);

        // No Clear()/re-add: _dnsList holds the same instances the ItemsControl
        // is bound to, and they now raise PropertyChanged for Active. Clearing
        // tore down and rebuilt every row just to change one colour.
    }

    public IReadOnlyList<string> GetHealthFindings() =>
        _svc.BuildHealthFindings(RamPercent, StoragePercent, FreeStorageGb);

    [RelayCommand]
    private void ToggleShield(PrivacyShield? shield)
    {
        if (shield == null) return;
        shield.Enabled = !shield.Enabled;
        TogglePrivacyShield(shield.Name, shield.Enabled);
    }

    public void RefreshStats()
    {
        var stats = _svc.GetRamAndStorageStats();
        RamPercent = Math.Round(stats.usedPercent, 1);
        var storage = _svc.GetStorageStats();
        StoragePercent = Math.Round(storage.usedPercent, 1);
        TotalRamGb = Math.Round(stats.totalBytes / 1_073_741_824.0, 1);
        FreeStorageGb = Math.Round(storage.totalGb - storage.usedGb, 1);

        // -1 means "no battery present", which is what a desktop reports.
        // The old code coerced that to 100 and displayed a fake charge.
        BatteryPercent = _svc.GetBatteryPercent();

        Score = ComputeScore();
        HealthScoreChanged?.Invoke(Score);
        SystemStatsChanged?.Invoke();
    }

    /// <summary>
    /// Slower, more detailed probes. Kept separate from RefreshStats because
    /// GetCpuPercent sleeps a full second, which is unacceptable on the
    /// 5-second poll path.
    /// </summary>
    public void RefreshDiagnostics()
    {
        try
        {
            CpuPercent = _svc.GetCpuPercent();

            var uptime = _svc.GetLastBootTime();
            UptimeText = uptime <= TimeSpan.Zero
                ? "—"
                : uptime.TotalDays >= 1
                    ? $"{(int)uptime.TotalDays}d {uptime.Hours}h"
                    : $"{uptime.Hours}h {uptime.Minutes}m";

            var disk = _svc.GetDiskHealth();
            DiskHealthText = disk switch
            {
                { Available: false } => "Not reported (VM or unsupported)",
                { Status: "Healthy" } => $"Healthy — {disk.Model}",
                _ => $"{disk.Status} — {disk.Model}"
            };

            var procs = _svc.GetTopMemoryProcesses(5);
            TopProcesses.Clear();
            foreach (var p in procs) TopProcesses.Add(p);
            TopMemoryText = procs.Count == 0
                ? "—"
                : string.Join("  ·  ", procs.Take(3).Select(p => $"{p.Name} {p.WorkingSetMb * 1024:F0} MB"));
        }
        catch (Exception ex)
        {
            Log($"Diagnostics refresh failed: {ex.Message}", "warning");
        }
    }

    /// <summary>
    /// Read-only scan. Returns a detached list so callers on a background
    /// thread can hold a stable snapshot — the UI no longer binds a shared
    /// collection that the 5s poll can mutate underneath it.
    /// </summary>
    public IReadOnlyList<CleanupCandidate> ScanCleanupAndReturn()
    {
        var candidates = _svc.ScanTempForCleanup();
        var totalGb = candidates.Sum(c => c.Bytes) / 1_073_741_824.0;
        Log($"Scan found {candidates.Count} item(s), {totalGb:F2} GB reclaimable.", "system");
        return candidates;
    }

    // Weighted so the score can genuinely reach 0 and 100. Previously it started
    // at a flat 50 base, so a machine with 99% RAM and a full disk still
    // reported ~51 and the ring never dipped into the red band.
    private double ComputeScore()
    {
        // Free RAM is worth the most: 55% of the total.
        var ram = HeadroomScore(RamPercent, 50) * 0.55;
        // Free disk: 30%. Storage gets a more generous comfort band since
        // 70% full is still a healthy disk.
        var storage = HeadroomScore(StoragePercent, 70) * 0.30;
        // Privacy shields enabled: up to 10%, scaled by how many are on.
        var privacy = Shields.Count == 0 ? 0 : CountEnabledShields() / (double)Shields.Count * 10.0;
        // 5% for having reclaimed space at least once this session.
        var reclaimed = ReclaimedGb > 0 ? 5.0 : 0.0;
        return Math.Clamp(ram + storage + privacy + reclaimed, 0, 100);
    }

    /// <summary>
    /// Full marks while usage stays at or under <paramref name="comfort"/>,
    /// then decays linearly to zero at 100%. Without this, a machine sitting
    /// at a perfectly normal 30% RAM was docked the same points as one at 95%,
    /// which made the score read permanently mediocre.
    /// </summary>
    private static double HeadroomScore(double usedPercent, double comfort)
    {
        if (usedPercent <= comfort) return 100;
        if (usedPercent >= 100) return 0;
        return (100 - usedPercent) / (100 - comfort) * 100.0;
    }

    [RelayCommand]
    private async Task RunOptimizationAsync(object? candidates)
    {
        if (IsOptimizing) return;
        IsOptimizing = true;
        StatusMessage = "Running optimization...";
        Log("═ STARTING FULL OPTIMIZATION ═", "system");

        try
        {
            StatusMessage = "Scanning temp folders…";

            // Everything below is blocking I/O. Running it on the UI thread froze the
            // window for the whole operation.
            //
            // candidates is the preview the user actually saw. When null we scan
            // first, so the one-tap button still works standalone.
            var target = candidates as IReadOnlyCollection<CleanupCandidate>
                         ?? await Task.Run(ScanCleanupAndReturn);
            var reclaimedBytes = await Task.Run(() => _svc.CleanTempAndCache(target));

            Log("Flushing standby memory...", "system");
            await Task.Run(() => _svc.FlushStandbyMemory());
            await Task.Run(() => _svc.FlushDnsCache());

            var reclaimedGb = Math.Round(reclaimedBytes / 1_073_741_824.0, 2);

            // Deleting files cannot be undone, so this is recorded honestly as
            // DisplayOnly rather than advertising a fake "Revert".
            var tx = new OptimizationTransaction
            {
                Title = "1-Tap Optimization",
                Description = $"Cleared {reclaimedGb} GB from temp folders, flushed standby memory and DNS cache.",
                Time = DateTime.Now,
                Kind = UndoKind.DisplayOnly,
                PreviousScore = Score,
                PreviousRam = RamPercent,
                PreviousStorage = StoragePercent,
                ReclaimedGb = reclaimedGb
            };

            ReclaimedGb += reclaimedGb;
            await Task.Run(RefreshStats);

            UndoHistory.Insert(0, tx);
            _svc.PushUndoTransaction(tx);

            Log($"Optimization complete. Score: {Score:F0}/100. Reclaimed {reclaimedGb} GB.", "success");
            StatusMessage = $"Optimization complete. +{reclaimedGb} GB reclaimed.";
        }
        catch (Exception ex)
        {
            Log($"Optimization error: {ex.Message}", "warning");
            StatusMessage = "Optimization encountered an error.";
        }
        finally
        {
            IsOptimizing = false;
        }
    }

    [RelayCommand]
    private void LoadBloatware()
    {
        BloatwareApps.Clear();
        foreach (var app in _svc.GetUnusedApps())
            BloatwareApps.Add(app);
        Log($"Bloatware scan complete. Found {BloatwareApps.Count} candidates.", "system");
    }

    [RelayCommand]
    private void UninstallApp(BloatwareApp? app)
    {
        if (app == null) return;
        Log($"Uninstalling: {app.Name}...", "system");
        var success = _svc.UninstallApp(app.Package, app.Name);
        if (success)
        {
            BloatwareApps.Remove(app);

            // This one IS genuinely reversible: we re-register the package
            // from its install location on revert.
            var tx = new OptimizationTransaction
            {
                Title = $"Purged {app.Name}",
                Description = $"Uninstalled {app.Package}. Reason: {app.Reason}",
                Time = DateTime.Now,
                Kind = UndoKind.ReinstallApp,
                UndoPayload = app.Id,
                PreviousScore = Score,
                PreviousRam = RamPercent,
                PreviousStorage = StoragePercent
            };
            UndoHistory.Insert(0, tx);
            _svc.PushUndoTransaction(tx);
            Log($"Uninstalled {app.Name}.", "success");
        }
    }

    [RelayCommand]
    private async Task RevertTransaction(OptimizationTransaction? tx)
    {
        if (tx == null) return;

        if (!tx.Reversable)
        {
            // Previously this silently rewrote the displayed numbers and logged
            // "Reverted", implying files came back. They cannot. Be explicit.
            LastRevertMessage = "This action was recorded for history only and cannot be reversed. " +
                                "Only the recorded score history was rolled back.";
            UndoHistory.Remove(tx);
            _svc.RemoveUndoTransaction(tx);
            RefreshStats();
            Log($"Dismissed {tx.Title}: {LastRevertMessage}", "warning");
            return;
        }

        var (ok, message) = await Task.Run(() =>
        {
            var (success, msg) = (_svc.TryRevert(tx, out var m), m);
            return (success, msg);
        });

        LastRevertMessage = message;

        if (ok)
        {
            UndoHistory.Remove(tx);
            _svc.RemoveUndoTransaction(tx);
            Log($"Reverted: {tx.Title}. {message}", "success");
        }
        else
        {
            Log($"Revert failed for {tx.Title}: {message}", "warning");
        }
        RefreshStats();
    }

    /// <summary>Feedback from the last revert attempt, shown in the UI.</summary>
    public string LastRevertMessage { get; set; } = "";

    public void LoadUndoHistory()
    {
        UndoHistory.Clear();
        foreach (var tx in _svc.GetUndoTransactions())
            UndoHistory.Add(tx);
    }

    [RelayCommand]
    private void ScanCleanupPreview()
    {
        CleanupPreview.Clear();
        foreach (var c in ScanCleanupAndReturn()) CleanupPreview.Add(c);
    }

    [RelayCommand]
    private void SelectDns(DnsResolver? dns)
    {
        if (dns == null) return;
        foreach (var d in _dnsList)
        {
            d.Active = d.Id == dns.Id;
            if (d.Id == dns.Id)
            {
                var ok = _svc.SetDns(d.Ip, d.Name);
                if (ok) ActiveDns = d.Name;
                else SyncDnsFromSystem();
                Log(ok
                    ? $"DNS switched to {d.Name} ({d.Ip})."
                    : $"DNS switch to {d.Name} needs Administrator. Right-click WinCare Pro and choose Run as administrator.",
                    ok ? "success" : "warning");
            }
        }
        RefreshDnsList();
    }

    private void RefreshDnsList()
    {
        // Intentionally a no-op. _dnsList items are the same objects already in
        // DnsResolvers and they notify on Active. Kept as a seam in case the
        // list source ever needs rebuilding.
    }

    [RelayCommand]
    private void FlushRamStandby()
    {
        Log("Flushing RAM standby buffer...", "system");
        _svc.FlushStandbyMemory();
        RefreshStats();
        Log("RAM standby flushed.", "success");
    }

    [RelayCommand]
    private void ClearLogs()
    {
        Logs.Clear();
        Log("Logger buffer flushed. Listening for changes...", "system");
    }
}
