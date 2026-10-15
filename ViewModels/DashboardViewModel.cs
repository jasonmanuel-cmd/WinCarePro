using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using WinCareDesktop.Models;
using WinCareDesktop.Services;

namespace WinCareDesktop.ViewModels;

public partial class DashboardViewModel : ViewModelBase
{
    private readonly MainViewModel _mainVm;
    private readonly SystemOptimizerService _svc;

    public MainViewModel Main => _mainVm;

    [ObservableProperty]
    private string _optimizationStatus = "Ready to scan and optimize.";

    [ObservableProperty]
    private int _optimizationProgress = 0;

    [ObservableProperty]
    private bool _isBusy = false;

    public DashboardViewModel(MainViewModel mainVm)
    {
        _mainVm = mainVm;
        _svc = mainVm.Service;
        Title = "System Dashboard";
        Subtitle = "Real-time system telemetry and one-click health optimization";
    }

    [RelayCommand]
    public async Task RunQuickOptimizeAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        OptimizationProgress = 10;
        OptimizationStatus = "Analyzing temporary files and memory caches...";

        try
        {
            await Task.Delay(400); // UI feedback
            OptimizationProgress = 40;
            OptimizationStatus = "Purging orphaned temp files and user caches...";

            await _mainVm.RunOptimizationCommand.ExecuteAsync(null);

            OptimizationProgress = 85;
            OptimizationStatus = "Refreshing system telemetry & memory state...";
            await Task.Delay(300);

            _mainVm.RefreshStats();
            OptimizationProgress = 100;
            OptimizationStatus = $"Optimization complete! Reclaimed {_mainVm.ReclaimedGb:F2} GB.";
            _mainVm.Log($"Quick Optimize finished. Health score updated to {_mainVm.Score:F0}%", "success");
        }
        catch (Exception ex)
        {
            OptimizationStatus = $"Optimization encountered an error: {ex.Message}";
            _mainVm.Log($"Optimization failed: {ex.Message}", "error");
        }
        finally
        {
            await Task.Delay(1500);
            IsBusy = false;
            OptimizationProgress = 0;
            OptimizationStatus = "Ready to scan and optimize.";
        }
    }

    [RelayCommand]
    public void RefreshTelemetry()
    {
        _mainVm.RefreshStats();
    }
}
