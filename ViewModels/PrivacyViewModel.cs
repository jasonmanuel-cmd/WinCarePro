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
    private string _statusText = "Only verified off settings count as protected. Unknown means unreadable, absent or unsupported.";

    public PrivacyViewModel(MainViewModel main)
    {
        _main = main;
        Shields.CollectionChanged += (_, e) =>
        {
            if (e.NewItems != null) foreach (PrivacyShield shield in e.NewItems) shield.PropertyChanged += (_, _) => UpdateCount();
            UpdateCount();
        };
        Title = "Windows Privacy Shield";
        Subtitle = "Control the current-user advertising ID and tailored experiences; this does not disable Windows telemetry";
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
        StatusText = _main.StatusMessage;
    }

    [RelayCommand]
    public void EnableAll()
    {
        foreach (var s in Shields)
        {
            if (!s.Enabled) _main.TogglePrivacyShield(s.Name, true);
        }
        UpdateCount();
        StatusText = $"{BlockedCount} of {Shields.Count} settings verified off. Review history/log for failures.";
    }

    [RelayCommand]
    public void DisableAll()
    {
        foreach (var s in Shields)
        {
            if (s.Blocked != false) _main.TogglePrivacyShield(s.Name, false);
        }
        UpdateCount();
        StatusText = $"{BlockedCount} settings remain off. This sets values on; use History to restore previous values.";
    }
}
