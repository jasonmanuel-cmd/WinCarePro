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
        Subtitle = "Review all modifications made by WinCare Pro and cleanly rollback any action with one click";
    }

    [RelayCommand]
    public void Rollback(OptimizationTransaction? tx)
    {
        if (tx == null) return;
        _main.RevertTransactionCommand.Execute(tx);
        StatusText = $"Rolled back: {tx.Title}";
    }

    [RelayCommand]
    public void ClearLogs()
    {
        Logs.Clear();
        _main.Log("Event log cleared.", "system");
        StatusText = "Logs cleared.";
    }
}
