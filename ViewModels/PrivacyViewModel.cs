using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using WinCareDesktop.Models;

namespace WinCareDesktop.ViewModels;

public partial class PrivacyViewModel : ViewModelBase
{
    private readonly MainViewModel _main;

    public ObservableCollection<PrivacyShield> Shields => _main.Shields;

    [ObservableProperty]
    private int _blockedCount;

    [ObservableProperty]
    private string _statusText = "Privacy shields active.";

    public PrivacyViewModel(MainViewModel main)
    {
        _main = main;
        Title = "Windows Privacy Shield";
        Subtitle = "Prevent Windows from collecting diagnostic telemetry, location data, and advertising identifiers";
        UpdateCount();
    }

    private void UpdateCount()
    {
        BlockedCount = Shields.Count(s => s.Enabled);
    }

    [RelayCommand]
    public void ToggleShield(PrivacyShield? shield)
    {
        if (shield == null) return;
        var newStatus = !shield.Enabled;
        _main.TogglePrivacyShield(shield.Name, newStatus);
        UpdateCount();
        StatusText = $"{shield.Name} is now {(newStatus ? "Protected" : "Default")}.";
    }

    [RelayCommand]
    public void EnableAll()
    {
        foreach (var s in Shields)
        {
            if (!s.Enabled) _main.TogglePrivacyShield(s.Name, true);
        }
        UpdateCount();
        StatusText = "All privacy protections enabled.";
    }

    [RelayCommand]
    public void DisableAll()
    {
        foreach (var s in Shields)
        {
            if (s.Enabled) _main.TogglePrivacyShield(s.Name, false);
        }
        UpdateCount();
        StatusText = "Restored Windows default telemetry settings.";
    }
}
