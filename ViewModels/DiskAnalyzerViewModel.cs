using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using WinCareDesktop.Services;

namespace WinCareDesktop.ViewModels;

public partial class DiskAnalyzerViewModel : ViewModelBase
{
    private readonly DiskAnalyzerService _analyzer = new();
    private CancellationTokenSource? _cts;

    [ObservableProperty]
    private string _currentPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private string _statusText = "Ready to analyze disk space.";

    [ObservableProperty]
    private string _systemCacheSummary = "Click 'Measure System Caches' to inspect Recycle Bin & Windows Update staging size.";

    [ObservableProperty]
    private bool _isMeasuringCaches;

    [ObservableProperty]
    private DiskItem? _selectedItem;

    public ObservableCollection<DiskItem> Items { get; } = new();
    public ObservableCollection<SafeCacheCategorySummary> SafeCacheCategories { get; } = new();
    public ObservableCollection<DuplicateFileGroup> DuplicateGroups { get; } = new();

    public DeepCacheScannerService DeepCacheScanner { get; set; } = new();
    public DuplicateFileFinderService DuplicateFinder { get; set; } = new();
    public SystemOptimizerService? History { get; set; }
    public Action? OnHistoryRecorded { get; set; }

    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasDuplicateResults))]
    private bool _isScanningDuplicates;

    public bool HasDuplicateResults => DuplicateGroups.Count > 0;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanCleanSafeCaches))]
    private bool _isScanningSafeCaches;

    public bool CanCleanSafeCaches =>
        !SafetyMode.Enabled && !IsScanningSafeCaches && SafeCacheCategories.Any(c => c.Bytes > 0);

    public Func<IReadOnlyList<SafeCacheCategorySummary>, bool> ConfirmDeepCacheClean { get; set; } = cats =>
    {
        var totalBytes = cats.Sum(c => c.Bytes);
        var totalFiles = cats.Sum(c => c.FileCount);
        var breakdown = string.Join("\n", cats.Where(c => c.Bytes > 0).Select(c => $"• {c.Name}: {c.FormattedSize} ({c.FileCount} files)"));
        return System.Windows.MessageBox.Show(
            $"Clean {totalFiles} previewed cache files ({WinCareDesktop.Models.InstalledProgram.FormatBytes(totalBytes)})?\n\n" +
            $"{breakdown}\n\n" +
            "Saved passwords, cookies, browsing history, and active logins are NEVER touched. Files currently locked by open browsers or Explorer are safely skipped. Deleted cache files cannot be restored.",
            "Confirm Safe Cache Cleanup",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning) == System.Windows.MessageBoxResult.Yes;
    };

    private readonly Func<string, bool> _confirm;
        private readonly Action<string> _recycle;

        public DiskAnalyzerViewModel(Func<string, bool>? confirm = null, Action<string>? recycle = null)
        {
            _confirm = confirm ?? (path => System.Windows.MessageBox.Show($"Move this exact target to the Recycle Bin?\n\n{path}\n\nNon-recyclable targets are refused. There is no permanent-delete fallback.", "Confirm removal", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning, System.Windows.MessageBoxResult.No) == System.Windows.MessageBoxResult.Yes);
            _recycle = recycle ?? Recycle;
        Title = "Disk Space Analyzer";
        Subtitle = "Partial estimates: top 50 items; folders measured only two levels deep; inaccessible items skipped";
    }

    public override void OnNavigatedTo()
    {
        if (Items.Count == 0 && !IsScanning)
        {
            _ = StartScanAsync();
        }
    }

    [RelayCommand]
    public async Task StartScanAsync()
    {
        if (IsScanning) return;
        IsScanning = true;
        StatusText = $"Scanning {CurrentPath}...";
        Items.Clear();

        _cts = new CancellationTokenSource();
        var progress = new Progress<string>(msg => StatusText = msg);

        try
        {
            var results = await _analyzer.ScanDirectoryAsync(CurrentPath, progress, _cts.Token);
            foreach (var item in results)
            {
                Items.Add(item);
            }
            StatusText = $"Partial scan: {Items.Count} top items (maximum 50). Folder sizes cover two levels; unreadable entries skipped.";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Scan cancelled.";
        }
        catch (Exception ex)
        {
            StatusText = $"Scan failed: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
        }
    }

    [RelayCommand]
    public void CancelScan()
    {
        _cts?.Cancel();
    }

    [RelayCommand]
    public async Task SelectQuickTargetAsync(string? target)
    {
        if (IsScanning || string.IsNullOrWhiteSpace(target)) return;
        var resolved = target switch
        {
            "Profile" => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads" => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
            "Temp" => Path.GetTempPath(),
            "LocalAppData" => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            _ => ""
        };
        if (!string.IsNullOrWhiteSpace(resolved) && Directory.Exists(resolved))
        {
            CurrentPath = resolved;
            await StartScanAsync();
        }
    }

    [RelayCommand]
    public async Task MeasureSystemCachesAsync()
    {
        if (IsMeasuringCaches || IsScanningSafeCaches) return;
        IsMeasuringCaches = true;
        IsScanningSafeCaches = true;
        SystemCacheSummary = "Scanning Browser HTTP/Code caches, Shader caches, Temp, CrashDumps, Recycle Bin & Update cache…";
        try
        {
            var (categories, recycleBytes, updateBytes) = await Task.Run(() =>
            {
                var cats = DeepCacheScanner.ScanSafeCaches();
                var cleanup = new StorageCleanupService();
                return (cats, cleanup.GetRecycleBinSize(), cleanup.GetWindowsUpdateCacheSize());
            });

            SafeCacheCategories.Clear();
            foreach (var c in categories) SafeCacheCategories.Add(c);
            OnPropertyChanged(nameof(CanCleanSafeCaches));

            var safeTotal = categories.Sum(c => c.Bytes);
            var nonZero = string.Join(" · ", categories.Where(c => c.Bytes > 0).Select(c => $"{c.Name}: {c.FormattedSize}"));
            if (string.IsNullOrWhiteSpace(nonZero)) nonZero = "Safe app/browser caches: 0 B";

            SystemCacheSummary =
                $"Safe Regenerable Caches ({WinCareDesktop.Models.InstalledProgram.FormatBytes(safeTotal)}): {nonZero}  |  " +
                $"Recycle Bin: {WinCareDesktop.Models.InstalledProgram.FormatBytes(recycleBytes)} · Windows Update Cache: {WinCareDesktop.Models.InstalledProgram.FormatBytes(updateBytes)}";
        }
        catch (Exception ex)
        {
            SystemCacheSummary = $"Cache measurement incomplete: {ex.Message}";
        }
        finally
        {
            IsMeasuringCaches = false;
            IsScanningSafeCaches = false;
        }
    }

    [RelayCommand]
    public async Task CleanSafeCachesAsync()
    {
        if (!CanCleanSafeCaches) return;
        var snapshot = SafeCacheCategories.Where(c => c.Bytes > 0).ToArray();
        if (snapshot.Length == 0) return;
        if (!ConfirmDeepCacheClean(snapshot))
        {
            SystemCacheSummary = "Safe cache cleanup cancelled; no files deleted.";
            return;
        }

        IsScanningSafeCaches = true;
        try
        {
            var (reclaimed, deleted, skipped) = await Task.Run(() => DeepCacheScanner.CleanVerifiedSnapshot(snapshot));
            var msg = $"Safe cache cleanup deleted {deleted} file(s) ({WinCareDesktop.Models.InstalledProgram.FormatBytes(reclaimed)}); {skipped} active/locked file(s) skipped.";
            SystemCacheSummary = msg;
            if (History != null && deleted > 0)
            {
                History.PushUndoTransaction(new WinCareDesktop.Models.OptimizationTransaction
                {
                    Title = "Safe Browser/Shader/Temp Cache Cleanup",
                    Description = msg,
                    Kind = WinCareDesktop.Models.UndoKind.DisplayOnly,
                    ReclaimedGb = reclaimed / 1_073_741_824.0,
                    Time = DateTime.Now
                });
                OnHistoryRecorded?.Invoke();
            }
            SafeCacheCategories.Clear();
            OnPropertyChanged(nameof(CanCleanSafeCaches));
        }
        catch (Exception ex)
        {
            SystemCacheSummary = $"Safe cache cleanup failed: {ex.Message}";
        }
        finally
        {
            IsScanningSafeCaches = false;
        }
    }

    [RelayCommand]
    public async Task ScanDuplicatesAsync()
    {
        if (IsScanning || IsScanningDuplicates) return;
        IsScanningDuplicates = true;
        DuplicateGroups.Clear();
        OnPropertyChanged(nameof(HasDuplicateResults));
        StatusText = $"Scanning {CurrentPath} for exact SHA-256 duplicate files (≥ 1 MB)…";
        try
        {
            using var dupCts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            var groups = await DuplicateFinder.FindDuplicatesAsync(CurrentPath, DuplicateFileFinderService.DefaultMinFileBytes, 3000, dupCts.Token);
            foreach (var g in groups) DuplicateGroups.Add(g);
            OnPropertyChanged(nameof(HasDuplicateResults));

            var totalReclaim = groups.Sum(g => g.ReclaimableBytes);
            StatusText = groups.Count == 0
                ? "Duplicate scan complete: no exact duplicate files ≥ 1 MB found in this folder."
                : $"Found {groups.Count} exact SHA-256 duplicate group(s) ({WinCareDesktop.Models.InstalledProgram.FormatBytes(totalReclaim)} reclaimable). Review carefully before moving extras to Recycle Bin.";
        }
        catch (Exception ex)
        {
            StatusText = $"Duplicate scan stopped: {ex.Message}";
        }
        finally
        {
            IsScanningDuplicates = false;
        }
    }

    [RelayCommand]
    public void RecycleDuplicateExtra(DuplicateFileGroup? group)
    {
        if (group == null || group.Paths.Count < 2) return;
        var extraPath = group.ExtraFilePath;
        DeleteItem(new DiskItem
        {
            FullPath = extraPath,
            Name = Path.GetFileName(extraPath),
            Bytes = group.FileSizeBytes,
            IsDirectory = false,
            Category = "Duplicate"
        });
        if (!File.Exists(extraPath))
        {
            DuplicateGroups.Remove(group);
            OnPropertyChanged(nameof(HasDuplicateResults));
        }
    }

    [RelayCommand]
    public void OpenInExplorer(DiskItem? item)
    {
        var target = item?.FullPath ?? CurrentPath;
        if (File.Exists(target) || Directory.Exists(target))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = File.Exists(target) ? $"/select,\"{target}\"" : $"\"{target}\"",
                UseShellExecute = true
            });
        }
    }

    private static void Recycle(string path) => RecycleFileService.Recycle(path);
    private static string Snapshot(string path)
    {
        PathSafety.ValidateRemoval(path);
        var paths = Directory.Exists(path) ? Directory.EnumerateFileSystemEntries(path, "*", SearchOption.AllDirectories).Prepend(path) : new[] { path };
        return string.Join("\n", paths.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).Select(p =>
        {
            FileSystemInfo info = Directory.Exists(p) ? new DirectoryInfo(p) : new FileInfo(p);
            return $"{p}|{info.Attributes}|{info.CreationTimeUtc.Ticks}|{info.LastWriteTimeUtc.Ticks}|{(info is FileInfo f ? f.Length : 0)}";
        }));
    }

    [RelayCommand]
    public void DeleteItem(DiskItem? item)
    {
        if (item == null) return;
        try
        {
            var path = Path.GetFullPath(item.FullPath);
            PathSafety.ValidateRemoval(path);
            var snapshot = Snapshot(path);
            if (!_confirm(path)) { StatusText = "Removal cancelled."; return; }
            PathSafety.ValidateRemoval(path);
            if (Snapshot(path) != snapshot) throw new IOException("Target changed during confirmation; review it again.");
            _recycle(path);
            if (File.Exists(path) || Directory.Exists(path)) throw new IOException("Target still exists; nothing was reported as removed.");
            Items.Remove(item);
            StatusText = $"Moved to Recycle Bin: {path}. Restore using Windows Recycle Bin.";
        }
        catch (Exception ex)
        {
            StatusText = $"Could not delete item: {ex.Message}";
        }
    }
}
