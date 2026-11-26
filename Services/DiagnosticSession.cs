using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace WinCareDesktop.Services;

public partial class DiagnosticSession : ObservableObject
{
    private readonly Func<CancellationToken, Task<DiagnosticSnapshot>> _reader;
    public DiagnosticSession(Func<CancellationToken, Task<DiagnosticSnapshot>> reader, DiagnosticSnapshot? initial = null)
    {
        _reader = reader;
        Snapshot = initial ?? new(null, null, null, null, null, null);
        _guidance = DashboardGuidance.Build(Snapshot, DateTimeOffset.UtcNow);
        if (initial?.CapturedAt != null) ScanStatus = "Retained sample. Rescan for a new observation.";
    }
    public DiagnosticSnapshot Snapshot { get; private set; }
    private IReadOnlyList<GuidanceItem> _guidance;
    public IReadOnlyList<GuidanceItem> Guidance => _guidance;
    private CancellationTokenSource? _scanCancellation;
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private string _scanStatus = "No guidance scan yet.";
    public string SampleStatus => Snapshot.CapturedAt is not { } at ? "Not scanned • live cards are separate samples"
        : $"Guidance sample: {at.ToLocalTime():g} • {(DateTimeOffset.UtcNow - at > TimeSpan.FromMinutes(1) || at > DateTimeOffset.UtcNow ? "Stale — rescan" : "Recent (within one minute)")}";

    public void UpdateFreshness()
    {
        var next = DashboardGuidance.Build(Snapshot, DateTimeOffset.UtcNow);
        if (!_guidance.SequenceEqual(next))
        {
            _guidance = next;
            OnPropertyChanged(nameof(Guidance));
        }
        OnPropertyChanged(nameof(SampleStatus));
    }

    [RelayCommand]
    public async Task RescanAsync()
    {
        if (IsScanning) return;
        IsScanning = true;
        using var cancellation = new CancellationTokenSource();
        _scanCancellation = cancellation;
        ScanStatus = "Reading RAM, system-drive capacity and privacy preferences… No settings are changed.";
        try
        {
            var result = await _reader(cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            Snapshot = result;
            OnPropertyChanged(nameof(Snapshot));
            ScanStatus = "Read-only scan complete. Missing readings are shown as unknown.";
        }
        catch (OperationCanceledException) { ScanStatus = "Scan cancelled. Previous sample retained; it may be stale."; }
        catch (Exception) { ScanStatus = "Read-only scan failed. Previous sample retained; retry. No settings were changed."; }
        finally
        {
            _scanCancellation = null;
            IsScanning = false;
            UpdateFreshness();
        }
    }

    [RelayCommand]
    public void CancelScan()
    {
        if (_scanCancellation == null) return;
        _scanCancellation.Cancel();
        ScanStatus = "Cancellation requested. Waiting for the current Windows read to return; late results will be discarded.";
    }
}
