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

    public Func<List<InstalledProgram>>? LoadPrograms { get; set; }
    public Func<List<InstalledProgram>>? LoadStorePrograms { get; set; }

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

        try
        {
            var inventory = await Task.Run(() =>
            {
                var apps = new List<InstalledProgram>();
                var errors = new List<string>();
                try { apps.AddRange(LoadPrograms != null ? LoadPrograms() : _service.GetInstalledPrograms()); }
                catch (Exception ex) { errors.Add("Desktop inventory: " + ex.Message); }
                // A supplied combined inventory remains a complete test seam unless Store is explicitly supplied.
                if (LoadStorePrograms != null || LoadPrograms == null)
                    try { apps.AddRange(LoadStorePrograms != null ? LoadStorePrograms() : _appx.GetStoreApps()); }
                    catch (Exception ex) { errors.Add("Store inventory: " + ex.Message); }
                return (apps, errors);
            });
            _allPrograms = inventory.apps;
            ApplyFilter();
            StatusText = inventory.errors.Count == 0
                ? $"Found {_allPrograms.Count} installed applications (current-user Store packages included)."
                : $"Partial inventory: {_allPrograms.Count} applications available. " + string.Join("; ", inventory.errors);
        }
        catch (Exception ex) { StatusText = "Inventory incomplete: " + ex.Message; }
        finally { IsLoading = false; }
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

        foreach (var p in filtered)
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

    public Func<string, bool> Confirm { get; set; } = text => System.Windows.MessageBox.Show(text, "Confirm uninstall", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning, System.Windows.MessageBoxResult.No) == System.Windows.MessageBoxResult.Yes;
    public SystemOptimizerService History { get; set; } = new();
    public Func<InstalledProgram, CancellationToken, Task<(bool Success, string Message)>>? Uninstall { get; set; }
    public Func<InstalledProgram, CancellationToken, Task<UninstallOutcome>>? UninstallWithOutcome { get; set; }
    private CancellationTokenSource? _uninstallCts;

    [RelayCommand]
    public void CancelUninstall() => _uninstallCts?.Cancel();

    [RelayCommand]
    public async Task UninstallSelectedAsync()
    {
        if (IsUninstalling) return;
        var targets = DisplayedApps.Where(x => x.IsSelected).ToList();
        if (targets.Count == 0) return;
        if (!Confirm("Uninstall these exact applications? Reinstallation may require your installer, license and data backup. This is not an undoable action.\n\n" + string.Join("\n", targets.Select(x => x.Name + " — " + (x.Program.Kind == UninstallKind.Appx ? x.Program.PackageFullName : InstalledProgramsService.TryBuildUninstallCommand(x.Program, out var executable, out var arguments, out _) ? executable + " " + arguments : "No valid uninstall command"))))) { StatusText = "Uninstall cancelled."; return; }
        IsUninstalling = true;
        _uninstallCts = new CancellationTokenSource();
        var outcomes = new List<UninstallOutcome>();
        try
        {
            foreach (var target in targets)
            {
                if (_uninstallCts.IsCancellationRequested) break;
                StatusText = $"Uninstalling {target.Name}...";
                UninstallOutcome result;
                try
                {
                    if (UninstallWithOutcome != null) result = await UninstallWithOutcome(target.Program, _uninstallCts.Token);
                    else if (Uninstall != null)
                    {
                        var legacy = await Uninstall(target.Program, _uninstallCts.Token);
                        result = new(legacy.Success ? UninstallState.VerifiedRemoved : UninstallState.Failed, legacy.Message);
                    }
                    else result = await RemoveAsync(target.Program, _uninstallCts.Token);
                }
                catch (OperationCanceledException) { result = new(UninstallState.Unknown, "Stopped waiting; operation may still be running. Refresh before retrying."); }
                catch (Exception ex) { result = new(UninstallState.Unknown, "Outcome unknown: " + ex.Message); }
                outcomes.Add(result);
                History.PushUndoTransaction(new OptimizationTransaction { Title = $"Uninstall: {target.Name}", Description = $"{result.State}: {result.Message}", Time = DateTime.Now, Kind = UndoKind.DisplayOnly });
                if (result.Success) { DisplayedApps.Remove(target); _allPrograms.Remove(target.Program); }
                else History.Log(result.Message, "warning");
            }
            int Count(UninstallState state) => outcomes.Count(o => o.State == state);
            StatusText = $"{Count(UninstallState.VerifiedRemoved)} verified removed; {Count(UninstallState.Failed)} failed; {Count(UninstallState.Pending)} pending; {Count(UninstallState.RebootRequired)} reboot required; {Count(UninstallState.Unknown)} unknown; {targets.Count - outcomes.Count} not attempted. Unverified items retained; see History.";
        }
        finally { IsUninstalling = false; SelectedCount = DisplayedApps.Count(x => x.IsSelected); _uninstallCts.Dispose(); _uninstallCts = null; }
    }

    private async Task<UninstallOutcome> RemoveAsync(InstalledProgram program, CancellationToken token)
    {
        if (program.Kind == UninstallKind.Appx)
        {
            try
            {
                var result = await Task.Run(() => _appx.RemoveStoreApp(program.PackageFullName, program.Name), token).WaitAsync(token);
                return new(result.Success ? UninstallState.VerifiedRemoved : UninstallState.Unknown, result.Message);
            }
            catch (OperationCanceledException) { return new(UninstallState.Unknown, "Stopped waiting. Store removal may still be running; outcome unknown. Refresh inventory before retrying."); }
        }
        if (!InstalledProgramsService.TryBuildUninstallCommand(program, out var file, out var args, out var warning)) return new(UninstallState.Failed, warning ?? "No valid uninstall command.");
        SafetyMode.RequireMutationAllowed();
        using var process = Process.Start(new ProcessStartInfo { FileName = file, Arguments = args, UseShellExecute = true });
        if (process == null) return new(UninstallState.Failed, "Uninstaller did not start.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { return new(UninstallState.Unknown, "Wait cancelled or timed out. The vendor uninstaller may still be running; inspect it before retrying."); }
        if (process.ExitCode != 0) return UninstallOutcome.FromClassicExit(process.ExitCode, null);
        bool? stillInstalled;
        try { stillInstalled = await Task.Run(() => _service.GetInstalledPrograms().Any(p => p.KeyPath == program.KeyPath), token); }
        catch { stillInstalled = null; }
        return UninstallOutcome.FromClassicExit(process.ExitCode, stillInstalled);
    }
}
