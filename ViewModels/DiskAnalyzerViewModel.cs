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
    private DiskItem? _selectedItem;

    public ObservableCollection<DiskItem> Items { get; } = new();

    public DiskAnalyzerViewModel()
    {
        Title = "Disk Space Analyzer";
        Subtitle = "Visualize large files, video downloads, and space-wasting folders";
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
            StatusText = $"Found {Items.Count} top items in {CurrentPath}.";
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

    [RelayCommand]
    public void DeleteItem(DiskItem? item)
    {
        if (item == null) return;
        try
        {
            if (item.IsDirectory && Directory.Exists(item.FullPath))
            {
                Directory.Delete(item.FullPath, true);
                Items.Remove(item);
                StatusText = $"Deleted folder {item.Name}.";
            }
            else if (!item.IsDirectory && File.Exists(item.FullPath))
            {
                File.Delete(item.FullPath);
                Items.Remove(item);
                StatusText = $"Deleted file {item.Name}.";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Could not delete item: {ex.Message}";
        }
    }
}
