using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinCareDesktop.Services;

namespace WinCareDesktop.ViewModels;

public partial class SchedulerViewModel : ViewModelBase
{
    private readonly SchedulerService _service = new();

    [ObservableProperty]
    private ScheduleConfig _config = new();

    [ObservableProperty]
    private string _statusMessage = "Automatic clean scheduler active.";

    public SchedulerViewModel()
    {
        Title = "Auto-Clean Automation";
        Subtitle = "Schedule background maintenance to keep Windows running at peak speed automatically";
        Config = _service.LoadConfig();
    }

    [RelayCommand]
    public void SaveSettings()
    {
        _service.SaveConfig(Config);
        StatusMessage = "Settings saved successfully.";
    }

    [RelayCommand]
    public void ToggleEnabled()
    {
        Config.IsEnabled = !Config.IsEnabled;
        SaveSettings();
        OnPropertyChanged(nameof(Config));
    }
}
