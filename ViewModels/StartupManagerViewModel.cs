using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using WinCareDesktop.Services;

namespace WinCareDesktop.ViewModels;

public partial class StartupManagerViewModel : ViewModelBase
{
    private readonly StartupManagerService _service = new();

    public ObservableCollection<StartupEntry> Entries { get; } = new();

    [ObservableProperty]
    private string _statusText = "Loading startup applications...";

    [ObservableProperty]
    private bool _isLoading;

    public StartupManagerViewModel()
    {
        Title = "Startup Application Manager";
        Subtitle = "Speed up Windows boot time by disabling unnecessary background programs";
    }

    public override void OnNavigatedTo()
    {
        Refresh();
    }

    [RelayCommand]
    public void Refresh()
    {
        IsLoading = true;
        Entries.Clear();

        try
        {
            var list = _service.GetStartupEntries();
            foreach (var item in list)
            {
                Entries.Add(item);
            }
            StatusText = $"Found {Entries.Count} startup programs.";
        }
        catch (Exception ex)
        {
            StatusText = $"Error loading startup programs: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public void ToggleItem(StartupEntry? entry)
    {
        if (entry == null) return;
        var newStatus = !entry.IsEnabled;
        if (_service.ToggleStartupEntry(entry, newStatus))
        {
            StatusText = $"Updated {entry.Name}: {(entry.IsEnabled ? "Enabled" : "Disabled")}.";
            Refresh();
        }
        else
        {
            StatusText = $"Failed to update {entry.Name}. Administrator privileges may be required.";
        }
    }
}
