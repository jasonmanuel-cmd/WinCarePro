using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinCareDesktop.Services;
namespace WinCareDesktop.ViewModels;
public partial class SchedulerViewModel : ViewModelBase, IDisposable
{
    private readonly SchedulerService _service;
    private readonly System.Windows.Threading.DispatcherTimer _timer;
    [ObservableProperty] private ScheduleConfig _config = new();
    [ObservableProperty] private string _statusMessage = "Disabled; confirmation required each session.";
    public Func<string, bool> Confirm { get; set; } = text => !SafetyMode.Enabled && System.Windows.MessageBox.Show(text, "Enable app-open auto-clean?", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning, System.Windows.MessageBoxResult.No) == System.Windows.MessageBoxResult.Yes;
    public SchedulerViewModel(SchedulerService? service = null)
    {
        _service = service ?? new SchedulerService();
        Title = "Auto-Clean Automation";
        Subtitle = "Temporary files only, while WinCare is open. No Windows background task.";
        Config = _service.LoadConfig();
        StatusMessage = _service.Status;
        _timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _timer.Tick += async (_, _) => { await _service.TickAsync(DateTime.Now); StatusMessage = _service.Status; };
    }
    [RelayCommand]
    public void SaveSettings()
    {
        _timer.Stop(); _service.Disable(); Config.IsEnabled = false;
        try { _service.SaveConfig(Config); StatusMessage = "Preferences saved. Enable and confirm again to activate."; }
        catch (Exception ex) { StatusMessage = "Not saved; disabled: " + ex.Message; }
        OnPropertyChanged(nameof(Config));
    }
    [RelayCommand]
    public void ToggleEnabled()
    {
        if (Config.IsEnabled) { SaveSettings(); return; }
        if (!Confirm($"Enable {Config.Frequency} cleanup at {Config.TimeOfDay} local time for this app session?\nOnly top-level user TEMP files older than 7 days; up to 100 files, 100 MiB and 15 seconds per run (1000 entries inspected). Future eligible files are included. No Recycle Bin, RAM, DNS, system caches or subfolders. Deletion is permanent. Closing WinCare stops automation. After sleep, one due attempt may run; missed days are never queued.")) return;
        try { _service.EnableForSession(Config, DateTime.Now); Config.IsEnabled = true; _timer.Start(); StatusMessage = _service.Status; }
        catch (Exception ex) { Config.IsEnabled = false; StatusMessage = "Disabled: " + ex.Message; }
        OnPropertyChanged(nameof(Config));
    }
    public void Dispose() { _timer.Stop(); _service.Dispose(); }
}
