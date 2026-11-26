using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using WinCareDesktop.Models;

namespace WinCareDesktop.ViewModels;

public partial class HistoryLogsViewModel : ViewModelBase
{
    private readonly MainViewModel _main;

    public ObservableCollection<OptimizationTransaction> UndoHistory => _main.UndoHistory;
    public ObservableCollection<string> Logs => _main.Logs;

    [ObservableProperty]
    private string _statusText = "Audit trail active.";

    public HistoryLogsViewModel(MainViewModel main)
    {
        _main = main;
        Title = "Undo History & Event Log";
        Subtitle = "Review outcomes; only verified privacy and DNS recovery records offer undo";
    }

    public override void OnNavigatedTo() => _main.LoadUndoHistory();

    [RelayCommand]
    public async Task Rollback(OptimizationTransaction? tx)
    {
        if (tx == null) return;
        StatusText = "Restoring; awaiting verified completion...";
        await _main.RevertTransactionCommand.ExecuteAsync(tx);
        StatusText = _main.LastRevertMessage;
        _main.RefreshPrivacyState();
        _main.SyncDnsFromSystem();
    }

    [RelayCommand]
    public void ClearLogs()
    {
        Logs.Clear();
        _main.Log("Event log cleared.", "system");
        StatusText = "Logs cleared.";
    }
}
