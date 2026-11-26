using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Windows;
using WinCareDesktop.Models;
using WinCareDesktop.Services;

namespace WinCareDesktop.ViewModels;

public partial class DashboardViewModel : ViewModelBase
{
    private readonly MainViewModel _mainVm;
    private readonly SystemOptimizerService _svc;
    private readonly PowerService _powerService = new();

    public MainViewModel Main => _mainVm;
    public DiagnosticSession Diagnostics { get; }
    public ObservableCollection<PowerPlan> PowerPlans { get; } = new();

    [ObservableProperty]
    private string _optimizationStatus = "The score is a weighted heuristic, not a diagnosis or security rating. Review measured evidence before making changes.";

    [ObservableProperty]
    private int _optimizationProgress = 0;

    [ObservableProperty]
    private bool _isBusy = false;

    [ObservableProperty]
    private string _activePowerPlanName = "Checking…";

    [ObservableProperty]
    private string _powerSourceText = "Desktop / AC or battery status pending";

    [ObservableProperty]
    private string _powerStatusMessage = "Windows power scheme controls CPU clock boost vs energy saving. Changes are reversible in History & Logs.";

    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanSwitchPowerPlan))]
    private bool _isSwitchingPower;

    public bool CanSwitchPowerPlan => !SafetyMode.Enabled && !IsSwitchingPower;

    public Func<PowerPlan, bool> ConfirmPowerPlan { get; set; } = plan =>
        MessageBox.Show(
            $"Switch the active Windows power plan to '{plan.Name}'?\n\n" +
            (plan.Name == "High performance"
                ? "High performance keeps CPU clocks boosted for maximum desktop responsiveness, which uses more power and battery.\n\n"
                : plan.Name == "Power saver"
                    ? "Power saver reduces CPU clock speed and energy draw, which can lower responsiveness under load.\n\n"
                    : "Balanced scales CPU clock speed automatically with demand.\n\n") +
            "Your current power plan will be recorded in History & Logs for one-click rollback.",
            "Confirm Windows Power Plan",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question) == MessageBoxResult.Yes;

    public DashboardViewModel(MainViewModel mainVm, DiagnosticSession? diagnostics = null)
    {
        _mainVm = mainVm;
        _svc = mainVm.Service;
        Diagnostics = diagnostics ?? new DiagnosticSession(new DiagnosticReader(_svc.GetRamStats, _svc.GetStorageStats, _svc.ReadPrivacyState).ReadAsync)
        {
            ChooseExportPath = () =>
            {
                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Save reviewed diagnostic summary locally",
                    FileName = "WinCare-diagnostics.json", DefaultExt = ".json",
                    Filter = "JSON report (*.json)|*.json", AddExtension = true, OverwritePrompt = true
                };
                return dialog.ShowDialog(Application.Current.MainWindow) == true ? dialog.FileName : null;
            }
        };
        Title = "System Dashboard";
        Subtitle = "Measured headroom, explained next steps and local diagnostics";
        RefreshPowerPlans();
    }

    public void RefreshPowerPlans()
    {
        PowerPlans.Clear();
        var plans = _powerService.GetPowerPlans();
        foreach (var p in plans)
        {
            PowerPlans.Add(p);
        }
        var active = plans.FirstOrDefault(p => p.Active);
        ActivePowerPlanName = active?.Name ?? "Custom / OEM plan";
        if (SafetyMode.Enabled)
        {
            PowerStatusMessage = "Safe mode active: power plan read-only; switching disabled.";
        }
    }

    public async Task RefreshPowerSourceAsync()
    {
        try
        {
            var onBattery = await Task.Run(() => _powerService.IsOnBattery());
            PowerSourceText = onBattery
                ? "Running on battery — Balanced or Power saver preserves runtime"
                : "Plugged in / desktop power — High performance keeps CPU boost ready";
        }
        catch
        {
            PowerSourceText = "Power source status unavailable";
        }
    }

    [RelayCommand]
    public async Task SwitchPowerPlanAsync(PowerPlan? plan)
    {
        if (plan == null || !CanSwitchPowerPlan) return;
        if (plan.Active)
        {
            PowerStatusMessage = $"{plan.Name} is already active.";
            return;
        }
        if (!ConfirmPowerPlan(plan))
        {
            PowerStatusMessage = "Power plan change cancelled; current scheme retained.";
            return;
        }

        IsSwitchingPower = true;
        try
        {
            var (ok, message) = await Task.Run(() => _svc.ApplyPowerPlan(plan.Guid, plan.Name, _powerService));
            PowerStatusMessage = message;
            RefreshPowerPlans();
            if (ok)
            {
                _mainVm.LoadUndoHistory();
            }
        }
        finally
        {
            IsSwitchingPower = false;
        }
    }

    [ObservableProperty]
    private bool _isProbingHardware;

    public string CpuDisplay => _mainVm.CpuPercent >= 0 ? $"{_mainVm.CpuPercent:F0}%" : "Not sampled";

    [RelayCommand]
    public async Task ProbeHardwareDiagnosticsAsync()
    {
        if (IsProbingHardware) return;
        IsProbingHardware = true;
        try
        {
            await Task.Run(() => _mainVm.RefreshDiagnostics());
            OnPropertyChanged(nameof(CpuDisplay));
        }
        finally
        {
            IsProbingHardware = false;
        }
    }

    [RelayCommand]
    public async Task RunQuickOptimizeAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        OptimizationStatus = "Preparing cleanup preview; confirmation required.";
        try
        {
            await _mainVm.RunOptimizationCommand.ExecuteAsync(null);
            OptimizationStatus = _mainVm.StatusMessage;
        }
        catch (Exception ex) { OptimizationStatus = "Cleanup failed: " + ex.Message; }
        finally { IsBusy = false; OptimizationProgress = 0; }
    }

    [RelayCommand]
    public Task RefreshTelemetryAsync() => Diagnostics.RescanAsync();

    [RelayCommand]
    public async Task OpenGuidanceAsync(GuidanceItem? item)
    {
        if (item == null) return;
        if (item.Panel == "Dashboard") { await Diagnostics.RescanAsync(); return; }
        var navigation = _mainVm.NavigationItems.FirstOrDefault(n => n.Title == item.Panel);
        if (navigation != null) _mainVm.NavigateCommand.Execute(navigation);
    }

    public override void OnNavigatedFrom() => Diagnostics.CancelScan();
    public override void OnNavigatedTo()
    {
        Diagnostics.UpdateFreshness();
        RefreshPowerPlans();
    }
}
