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

    [ObservableProperty, NotifyPropertyChangedFor(nameof(ScoreDisplay))] private double _score;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(RamUsageText), nameof(ScoreDisplay))] private double _ramPercent;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(StorageUsageText), nameof(ScoreDisplay))] private double _storagePercent;
    [ObservableProperty] private double _batteryPercent = -1;
    [ObservableProperty] private double _reclaimedGb;
    [ObservableProperty] private int _trackersBlocked;
    [ObservableProperty] private string _activeDns = "System default";
    [ObservableProperty] private bool _isOptimizing;
    [ObservableProperty] private string _statusMessage = "System ready.";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(RamUsageText), nameof(ScoreDisplay), nameof(RamCapacityText))] private double _totalRamGb;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(RamCapacityText))] private double _availableRamGb;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(StorageCapacityText))] private double _freeStorageGb;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(StorageUsageText), nameof(ScoreDisplay), nameof(StorageCapacityText))] private double _totalStorageGb;
    public string RamUsageText => TotalRamGb > 0 && DashboardGuidance.ValidPercent(RamPercent) ? $"{RamPercent:F0}%" : "Unknown";
    public string StorageUsageText => TotalStorageGb > 0 && DashboardGuidance.ValidPercent(StoragePercent) ? $"{StoragePercent:F0}%" : "Unknown";
    public string ScoreDisplay => RamUsageText == "Unknown" || StorageUsageText == "Unknown" ? "—" : $"{Score:F0}%";
    public string RamCapacityText => TotalRamGb > 0 ? $"Installed: {TotalRamGb:F1} GB • {AvailableRamGb:F1} GB available" : "RAM capacity unavailable";
    public string StorageCapacityText => TotalStorageGb > 0 ? $"Free space: {FreeStorageGb:F1} GB" : "Drive capacity unavailable";

    [ObservableProperty] private double _cpuPercent = -1;
    [ObservableProperty] private string _uptimeText = "—";
    [ObservableProperty] private string _diskHealthText = "Checking…";
    [ObservableProperty] private string _topMemoryText = "—";

    [ObservableProperty] private ViewModelBase _currentView = null!;
    public ObservableCollection<NavigationItem> NavigationItems { get; } = new();

    public MemoryViewModel MemoryVM { get; } = new();
    public DefenderViewModel DefenderVM { get; } = new();
    public OptionsViewModel OptionsVM { get; } = new();
    public DashboardViewModel DashboardVM { get; }
    public DiskAnalyzerViewModel DiskAnalyzerVM { get; }
    public StartupManagerViewModel StartupManagerVM { get; }
    public AppUninstallViewModel AppUninstallVM { get; }
    public PrivacyViewModel PrivacyVM { get; }
    public NetworkMonitorViewModel NetworkMonitorVM { get; }
    public SchedulerViewModel SchedulerVM { get; }
    public HistoryLogsViewModel HistoryLogsVM { get; }

    [RelayCommand]
    public void Navigate(NavigationItem? item)
    {
        if (item?.ViewModel == null) return;
        foreach (var nav in NavigationItems) nav.IsSelected = false;
        item.IsSelected = true;
        CurrentView?.OnNavigatedFrom();
        CurrentView = item.ViewModel;
        CurrentView.OnNavigatedTo();
    }

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
        // Initialize child ViewModels
        DashboardVM = new DashboardViewModel(this);
        DiskAnalyzerVM = new DiskAnalyzerViewModel { History = _svc, OnHistoryRecorded = LoadUndoHistory };
        StartupManagerVM = new StartupManagerViewModel { History = _svc, OnHistoryRecorded = LoadUndoHistory };
        AppUninstallVM = new AppUninstallViewModel { History = _svc };
        PrivacyVM = new PrivacyViewModel(this);
        NetworkMonitorVM = new NetworkMonitorViewModel(this);
        SchedulerVM = new SchedulerViewModel();
        HistoryLogsVM = new HistoryLogsViewModel(this);

        MemoryVM.NavigateToPanel = panel =>
        {
            var target = NavigationItems.FirstOrDefault(n => n.Title == panel);
            if (target != null) Navigate(target);
        };
        DefenderVM.NavigateToStartupApps = () =>
        {
            var target = NavigationItems.FirstOrDefault(n => n.Title == "Startup Apps");
            if (target != null) Navigate(target);
        };
        MemoryVM.TrimSelfWorkingSet = () =>
        {
            FlushRamStandby();
            return _svc.LogStream.LastOrDefault() ?? "Trimmed WinCare's working set.";
        };

        // Build navigation menu (uniform MDL2/Fluent glyphs, Options at bottom)
        var dashboardNav = new NavigationItem("Dashboard", "\uE9D9", "System overview & health", DashboardVM) { IsSelected = true };
        NavigationItems.Add(dashboardNav);
        NavigationItems.Add(new NavigationItem("Memory & Apps", "\uE950", "Sample resource demand", MemoryVM));
        NavigationItems.Add(new NavigationItem("Defender", "\uEA18", "Protection status & next steps", DefenderVM));
        NavigationItems.Add(new NavigationItem("Disk Analyzer", "\uEDA2", "Large file visualizer", DiskAnalyzerVM));
        NavigationItems.Add(new NavigationItem("Startup Apps", "\uEC4A", "Boot speed manager", StartupManagerVM));
        NavigationItems.Add(new NavigationItem("App Remover", "\uE7B8", "Uninstall software & bloat", AppUninstallVM));
        NavigationItems.Add(new NavigationItem("Privacy Shield", "\uE72E", "Two Windows privacy settings", PrivacyVM));
        NavigationItems.Add(new NavigationItem("Network & DNS", "\uE774", "Traffic & IPv4 DNS", NetworkMonitorVM));
        NavigationItems.Add(new NavigationItem("Auto-Clean", "\uE823", "Scheduled maintenance", SchedulerVM));
        NavigationItems.Add(new NavigationItem("History & Logs", "\uE81C", "Undo history & live logs", HistoryLogsVM));
        NavigationItems.Add(new NavigationItem("Options", "\uE713", "Lightweight app preferences", OptionsVM));

        CurrentView = DashboardVM;

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
        var ok = _svc.ApplyPrivacySetting(shieldName, enabled);
        RefreshPrivacyState();
        LoadUndoHistory();
        StatusMessage = ok ? "Privacy setting verified." : "Privacy change failed; see event log.";
        RefreshStats();
    }

    public void RefreshPrivacyState()
    {
        foreach (var shield in Shields)
            shield.Blocked = _svc.ReadPrivacyState(shield.Name);
        TrackersBlocked = CountEnabledShields();
    }

    private void LoadPrivacyFromSystem()
    {
        Shields.Add(new PrivacyShield { Name = "Advertising ID", Blocked = _svc.ReadPrivacyState("Advertising ID") });
        Shields.Add(new PrivacyShield { Name = "Tailored Experiences", Blocked = _svc.ReadPrivacyState("Tailored Experiences") });
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
        TogglePrivacyShield(shield.Name, !shield.Enabled);
    }

    public void RefreshStats()
    {
        var ram = _svc.GetRamStats();
        var storage = _svc.GetStorageStats();
        var battery = _svc.GetBatteryPercent();
        ApplyStatsSnapshot(ram, storage, battery);
    }

    public async Task RefreshStatsAsync()
    {
        var (ram, storage, battery) = await Task.Run(() =>
            (_svc.GetRamStats(), _svc.GetStorageStats(), _svc.GetBatteryPercent()));
        ApplyStatsSnapshot(ram, storage, battery);
    }

    private void ApplyStatsSnapshot(
        (double usedPercent, long totalBytes, long availableBytes) ram,
        (double usedPercent, double usedGb, double totalGb) storage,
        double batteryPercent)
    {
        RamPercent = Math.Round(ram.usedPercent, 1);
        StoragePercent = Math.Round(storage.usedPercent, 1);
        TotalStorageGb = storage.totalGb;
        TotalRamGb = Math.Round(ram.totalBytes / 1_073_741_824.0, 1);
        AvailableRamGb = Math.Round(ram.availableBytes / 1_073_741_824.0, 1);
        FreeStorageGb = Math.Round(storage.totalGb - storage.usedGb, 1);
        BatteryPercent = batteryPercent;

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
                : string.Join("  ·  ", procs.Take(3).Select(p => $"{p.Name} {p.WorkingSetMb:F0} MB"));
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

    public Func<IReadOnlyCollection<CleanupCandidate>, bool> ConfirmCleanup { get; set; } = items =>
    {
        if (SafetyMode.Enabled) return false;
        var files = items.SelectMany(c => c.IsDirectory ? c.Snapshot ?? new() : new List<CleanupCandidate> { c }).ToList();
        var window = new Window { Title = "Review exact cleanup snapshot — permanent deletion", Width = 750, Height = 550, WindowStartupLocation = WindowStartupLocation.CenterScreen };
        var panel = new System.Windows.Controls.DockPanel { Margin = new Thickness(16) };
        var actions = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new System.Windows.Controls.Button { Content = "Cancel", IsCancel = true, Padding = new Thickness(16, 8, 16, 8), Margin = new Thickness(8) };
        var apply = new System.Windows.Controls.Button { Content = $"Delete {files.Count} previewed files", Padding = new Thickness(16, 8, 16, 8), Margin = new Thickness(8) };
        apply.Click += (_, _) => window.DialogResult = true;
        actions.Children.Add(cancel); actions.Children.Add(apply); System.Windows.Controls.DockPanel.SetDock(actions, System.Windows.Controls.Dock.Bottom); panel.Children.Add(actions);
        panel.Children.Add(new System.Windows.Controls.TextBox { Text = $"Estimated bytes: {files.Sum(f => f.Bytes)}. Locked/changed files are skipped. No RAM or DNS changes. Files cannot be restored.\n\n" + string.Join("\n", files.Select(f => f.Path + " — " + f.Bytes + " bytes")), IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto });
        window.Content = panel;
        return window.ShowDialog() == true;
    };

    [RelayCommand]
    private async Task RunOptimizationAsync(object? candidates)
    {
        if (IsOptimizing) return;
        IsOptimizing = true;
        try
        {
            StatusMessage = "Preparing read-only cleanup preview...";
            var target = candidates as IReadOnlyCollection<CleanupCandidate> ?? await Task.Run(ScanCleanupAndReturn);
            if (!ConfirmCleanup(target)) { StatusMessage = "Cleanup cancelled; no files changed."; return; }
            StatusMessage = "Deleting only unchanged previewed files...";
            var bytes = await Task.Run(() => _svc.CleanTempAndCache(target));
            var gb = bytes / 1_073_741_824.0;
            ReclaimedGb += gb;
            StatusMessage = $"Cleanup: {bytes} bytes deleted; {_svc.LastCleanupErrors} errors/skips. Empty directories retained.";
            _svc.PushUndoTransaction(new OptimizationTransaction { Title = "Confirmed temporary-file cleanup", Description = StatusMessage, Kind = UndoKind.DisplayOnly, ReclaimedGb = gb, Time = DateTime.Now });
            LoadUndoHistory();
            RefreshStats();
            Log(StatusMessage, _svc.LastCleanupErrors == 0 ? "success" : "warning");
        }
        catch (Exception ex) { StatusMessage = "Cleanup incomplete: " + ex.Message; Log(StatusMessage, "warning"); }
        finally { IsOptimizing = false; }
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
    private async Task UninstallApp(BloatwareApp? app)
    {
        if (app == null) return;
        AppUninstallVM.DisplayedApps.Clear();
        AppUninstallVM.DisplayedApps.Add(new SelectableApp(new InstalledProgram { Name = app.Name, Kind = UninstallKind.Appx, PackageFullName = app.Package }) { IsSelected = true });
        await AppUninstallVM.UninstallSelectedAsync();
        if (AppUninstallVM.DisplayedApps.Count == 0) BloatwareApps.Remove(app);
        LoadUndoHistory();
        StatusMessage = AppUninstallVM.StatusText;
    }

    [RelayCommand]
    private async Task RevertTransaction(OptimizationTransaction? tx)
    {
        if (tx == null) return;

        if (!tx.Reversable)
        {
            // Previously this silently rewrote the displayed numbers and logged
            // "Reverted", implying files came back. They cannot. Be explicit.
            LastRevertMessage = "This history record is not safely reversible. No system state was changed; record retained.";
            Log(LastRevertMessage, "warning");
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
        Log(_svc.LogStream.LastOrDefault() ?? "Working-set trim finished; see log.");
    }

    [RelayCommand]
    private void ClearLogs()
    {
        Logs.Clear();
        Log("Logger buffer flushed. Listening for changes...", "system");
    }
}
