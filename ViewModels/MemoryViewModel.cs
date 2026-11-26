using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinCareDesktop.Services;

namespace WinCareDesktop.ViewModels;

public partial class MemoryViewModel : ViewModelBase
{
    public MemoryViewModel() : this(new NativeOptionalAppActions()) { }

    public Func<CancellationToken, Task<MemoryReading>> Read { get; set; } = NativeMemoryReader.ReadAsync;
    public Action<string>? NavigateToPanel { get; set; }
    public Func<string>? TrimSelfWorkingSet { get; set; }

    [ObservableProperty] private IReadOnlyList<MemoryRow> _rows = Array.Empty<MemoryRow>();
    [ObservableProperty] private IReadOnlyList<MemoryRow> _displayedRows = Array.Empty<MemoryRow>();
    [ObservableProperty] private string _filterText = "";
    [ObservableProperty] private bool _sortByCpu;
    [ObservableProperty] private string _summary = "No memory sample yet. RAM holds running work; disk storage holds files.";
    [ObservableProperty] private string _status = "Sample on demand. No apps will be closed.";
    [ObservableProperty] private bool _busy;

    private CancellationTokenSource? _cancel;

    partial void OnRowsChanged(IReadOnlyList<MemoryRow> value) => RebuildDisplayedRows();
    partial void OnFilterTextChanged(string value) => RebuildDisplayedRows();
    partial void OnSortByCpuChanged(bool value) => RebuildDisplayedRows();

    private void RebuildDisplayedRows()
    {
        IEnumerable<MemoryRow> query = Rows;
        if (!string.IsNullOrWhiteSpace(FilterText))
        {
            var term = FilterText.Trim();
            query = query.Where(r =>
                r.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                r.RoleHint.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                r.Pid.ToString().Contains(term, StringComparison.Ordinal));
        }

        query = SortByCpu
            ? query.OrderByDescending(r => r.CpuPercent ?? -1d).ThenByDescending(r => r.MemoryMb)
            : query.OrderByDescending(r => r.MemoryMb).ThenByDescending(r => r.CpuPercent ?? -1d);

        DisplayedRows = query.ToArray();
    }

    [RelayCommand] public void SortByRam() => SortByCpu = false;
    [RelayCommand] public void SortByCpuLoad() => SortByCpu = true;
    [RelayCommand] public void OpenStartupApps() => NavigateToPanel?.Invoke("Startup Apps");
    [RelayCommand] public void OpenAppRemover() => NavigateToPanel?.Invoke("App Remover");

    [RelayCommand]
    public void TrimWinCareRam()
    {
        if (TrimSelfWorkingSet != null)
        {
            Status = TrimSelfWorkingSet();
        }
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (Busy) return;
        Busy = true;
        using var cts = new CancellationTokenSource();
        _cancel = cts;
        Status = "Sampling memory and CPU…";
        try
        {
            var sample = await Read(cts.Token);
            cts.Token.ThrowIfCancellationRequested();
            Rows = sample.Rows;
            if (sample.TotalBytes > 0)
            {
                var usedGb = sample.UsedBytes / 1073741824d;
                var totalGb = sample.TotalBytes / 1073741824d;
                var availGb = Math.Max(0, sample.TotalBytes - sample.UsedBytes) / 1073741824d;
                Summary = $"RAM: {usedGb:F1} / {totalGb:F1} GB used ({availGb:F1} GB available) • sampled {sample.At.ToLocalTime():T}";
            }
            else
            {
                Summary = "RAM capacity unavailable; process observations are separate.";
            }
            Status = $"Read-only sample • top {Rows.Count} of {sample.Observed} readable processes. {sample.Skipped} inaccessible/exited/over-limit observations. CPU uses a short two-point sample; refresh to compare, not a speedup estimate.";
        }
        catch (OperationCanceledException)
        {
            Status = "Sampling cancelled. Previous observations retained; they may be stale.";
        }
        catch (Exception)
        {
            Status = "Memory read failed. Previous observations retained; retry. No apps were changed.";
        }
        finally
        {
            _cancel = null;
            Busy = false;
        }
    }

    [RelayCommand] public void Cancel() => _cancel?.Cancel();
    public override void OnNavigatedFrom() { Cancel(); CancelAppReview(); }
}
