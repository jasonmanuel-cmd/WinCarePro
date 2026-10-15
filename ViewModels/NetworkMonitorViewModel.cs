using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Diagnostics;
using WinCareDesktop.Models;
using WinCareDesktop.Services;

namespace WinCareDesktop.ViewModels;

public partial class NetworkMonitorViewModel : ViewModelBase
{
    private readonly MainViewModel _main;
    private readonly NetworkMonitorService _netSvc = new();

    public ObservableCollection<NetworkInterfaceInfo> Interfaces { get; } = new();
    public ObservableCollection<DnsResolver> DnsResolvers => _main.DnsResolvers;

    [ObservableProperty]
    private string _pingStatus = "Ready to test latency.";

    [ObservableProperty]
    private long _currentPingMs = -1;

    [ObservableProperty]
    private string _activeDnsName;

    public NetworkMonitorViewModel(MainViewModel main)
    {
        _main = main;
        _activeDnsName = main.ActiveDns;
        Title = "Network & DNS Optimization";
        Subtitle = "Monitor live adapter throughput, test network latency, and switch to high-speed secure DNS";
    }

    public override void OnNavigatedTo()
    {
        RefreshAdapters();
        _ = TestPingAsync();
    }

    [RelayCommand]
    public void RefreshAdapters()
    {
        Interfaces.Clear();
        foreach (var item in _netSvc.GetActiveInterfaces())
        {
            Interfaces.Add(item);
        }
    }

    [RelayCommand]
    public async Task TestPingAsync()
    {
        PingStatus = "Pinging Google DNS (8.8.8.8)...";
        CurrentPingMs = await _netSvc.PingHostAsync("8.8.8.8");
        if (CurrentPingMs >= 0)
        {
            PingStatus = $"Latency: {CurrentPingMs} ms (Excellent)";
        }
        else
        {
            PingStatus = "Ping request timed out or network disconnected.";
        }
    }

    [RelayCommand]
    public void SwitchDns(DnsResolver? dns)
    {
        if (dns == null) return;
        _main.SelectDnsCommand.Execute(dns);
        ActiveDnsName = _main.ActiveDns;
    }

    [RelayCommand]
    public void FlushDns()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "ipconfig",
                Arguments = "/flushdns",
                CreateNoWindow = true,
                UseShellExecute = false
            })?.WaitForExit(3000);

            _main.Log("Flushed Windows DNS Resolver Cache.", "success");
            PingStatus = "DNS Cache successfully flushed.";
        }
        catch (Exception ex)
        {
            _main.Log($"Failed to flush DNS: {ex.Message}", "error");
        }
    }
}
