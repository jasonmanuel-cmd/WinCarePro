using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using WinCareDesktop.Models;
using WinCareDesktop.Services;

namespace WinCareDesktop.ViewModels;

public partial class StartupManagerViewModel : ViewModelBase
{
    private readonly StartupManagerService _service;

    public ObservableCollection<StartupEntry> Entries { get; } = new();
    public ObservableCollection<StartupEntry> DisplayedEntries { get; } = new();

    public SystemOptimizerService? History { get; set; }
    public Action? OnHistoryRecorded { get; set; }

    [ObservableProperty]
    private string _statusText = "Loading startup applications...";

    [ObservableProperty]
    private string _impactSummary = "";

    [ObservableProperty]
    private string _filterText = "";

    [ObservableProperty]
    private bool _isLoading;

    public StartupManagerViewModel() : this(new StartupManagerService()) { }

    public StartupManagerViewModel(StartupManagerService service)
    {
        _service = service;
        Title = "Startup Application Manager";
        Subtitle = "Speed up Windows boot time and reduce idle RAM demand by disabling unneeded auto-start programs";
    }

    partial void OnFilterTextChanged(string value) => ApplyFilter();

    public override void OnNavigatedTo()
    {
        Refresh();
    }

    private static int ImpactRank(string impact) => impact switch
    {
        "High" => 0,
        "Medium" => 1,
        _ => 2
    };

    [RelayCommand]
    public void Refresh()
    {
        IsLoading = true;
        Entries.Clear();

        try
        {
            var list = _service.GetStartupEntries()
                .OrderByDescending(e => e.IsEnabled)
                .ThenBy(e => ImpactRank(e.Impact))
                .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var item in list)
            {
                Entries.Add(item);
            }

            var enabledCount = Entries.Count(e => e.IsEnabled);
            var highImpactEnabled = Entries.Count(e => e.IsEnabled && e.Impact == "High");
            ImpactSummary = $"{enabledCount} enabled at login • {highImpactEnabled} high-impact";
            StatusText = $"Found {Entries.Count} startup programs ({highImpactEnabled} active high-impact).";
            ApplyFilter();
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

    private void ApplyFilter()
    {
        DisplayedEntries.Clear();
        var term = FilterText?.Trim() ?? "";
        foreach (var item in Entries)
        {
            if (term.Length == 0 ||
                item.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                item.Command.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                item.Location.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                item.Impact.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                DisplayedEntries.Add(item);
            }
        }
    }

    [RelayCommand]
    public void ToggleItem(StartupEntry? entry)
    {
        if (entry == null) return;
        var previousEnabled = entry.IsEnabled;
        var newStatus = !previousEnabled;
        if (_service.ToggleStartupEntry(entry, newStatus))
        {
            if (History != null)
            {
                History.PushUndoTransaction(new OptimizationTransaction
                {
                    Title = $"Startup: {entry.Name} ({(newStatus ? "Enabled" : "Disabled")})",
                    Description = $"{entry.Location} • previous state captured for rollback.",
                    Kind = UndoKind.StartupEntry,
                    UndoPayload = $"v1|{Uri.EscapeDataString(entry.Location)}|{Uri.EscapeDataString(entry.Name)}|{previousEnabled}|{Uri.EscapeDataString(entry.Command)}",
                    Time = DateTime.Now
                });
                OnHistoryRecorded?.Invoke();
            }
            StatusText = $"Updated {entry.Name}: {(entry.IsEnabled ? "Enabled" : "Disabled")}. Undo available in History & Logs.";
            Refresh();
        }
        else
        {
            StatusText = $"Failed to update {entry.Name}. Administrator privileges or normal mode may be required.";
        }
    }
}
