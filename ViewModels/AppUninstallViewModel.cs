using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Diagnostics;
using WinCareDesktop.Models;
using WinCareDesktop.Services;

namespace WinCareDesktop.ViewModels;

public class SelectableApp : ObservableObject
{
    public InstalledProgram Program { get; }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public string Name => Program.Name;
    public string Publisher => string.IsNullOrWhiteSpace(Program.Publisher) ? "Unknown Publisher" : Program.Publisher;
    public string SizeText => Program.DisplaySize;
    public string TypeBadge => Program.Source;

    public SelectableApp(InstalledProgram prog)
    {
        Program = prog;
    }
}

public partial class AppUninstallViewModel : ViewModelBase
{
    private readonly InstalledProgramsService _service = new();
    private readonly AppxService _appx = new();
    private List<InstalledProgram> _allPrograms = new();

    public ObservableCollection<SelectableApp> DisplayedApps { get; } = new();

    [ObservableProperty]
    private string _searchFilter = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusText = "Loading installed applications...";

    [ObservableProperty]
    private int _selectedCount = 0;

    [ObservableProperty]
    private bool _isUninstalling;

    public AppUninstallViewModel()
    {
        Title = "Interactive App Uninstaller & Bloatware Remover";
        Subtitle = "Select and remove stubborn applications, desktop software, and pre-installed Windows bloatware";
    }

    public override void OnNavigatedTo()
    {
        if (_allPrograms.Count == 0)
        {
            _ = LoadAppsAsync();
        }
    }

    [RelayCommand]
    public async Task LoadAppsAsync()
    {
        IsLoading = true;
        StatusText = "Scanning installed applications and Windows Store packages...";
        DisplayedApps.Clear();

        await Task.Run(() =>
        {
            _allPrograms = _service.GetInstalledPrograms();
        });

        ApplyFilter();
        IsLoading = false;
        StatusText = $"Found {_allPrograms.Count} installed applications.";
    }

    partial void OnSearchFilterChanged(string value)
    {
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        DisplayedApps.Clear();
        var query = SearchFilter?.Trim().ToLowerInvariant() ?? string.Empty;

        var filtered = string.IsNullOrEmpty(query)
            ? _allPrograms
            : _allPrograms.Where(p => p.Name.ToLowerInvariant().Contains(query) ||
                                      p.Publisher.ToLowerInvariant().Contains(query));

        foreach (var p in filtered.Take(150)) // Smooth rendering limit
        {
            var item = new SelectableApp(p);
            item.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(SelectableApp.IsSelected))
                {
                    SelectedCount = DisplayedApps.Count(x => x.IsSelected);
                }
            };
            DisplayedApps.Add(item);
        }
        SelectedCount = DisplayedApps.Count(x => x.IsSelected);
    }

    [RelayCommand]
    public void SelectAll(bool select)
    {
        foreach (var item in DisplayedApps)
        {
            item.IsSelected = select;
        }
        SelectedCount = DisplayedApps.Count(x => x.IsSelected);
    }

    [RelayCommand]
    public async Task UninstallSelectedAsync()
    {
        var targets = DisplayedApps.Where(x => x.IsSelected).ToList();
        if (targets.Count == 0) return;

        IsUninstalling = true;
        int completed = 0;

        foreach (var target in targets)
        {
            StatusText = $"Uninstalling {target.Name}... ({completed + 1}/{targets.Count})";
            await Task.Run(() =>
            {
                if (target.Program.Kind == UninstallKind.Appx)
                {
                    _appx.RemoveStoreApp(target.Program.PackageFullName, target.Program.Name);
                }
                else
                {
                    if (InstalledProgramsService.TryBuildUninstallCommand(target.Program, out var file, out var args, out _))
                    {
                        try
                        {
                            var proc = Process.Start(new ProcessStartInfo
                            {
                                FileName = file,
                                Arguments = args,
                                UseShellExecute = true
                            });
                            proc?.WaitForExit(60000); // 1-minute timeout
                        }
                        catch { }
                    }
                }
            });
            completed++;
            DisplayedApps.Remove(target);
            _allPrograms.Remove(target.Program);
        }

        StatusText = $"Successfully processed {completed} program(s).";
        IsUninstalling = false;
        SelectedCount = 0;
    }
}
