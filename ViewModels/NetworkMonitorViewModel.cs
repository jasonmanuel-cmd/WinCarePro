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
    [ObservableProperty]
    private NetworkInterfaceInfo? _selectedAdapter;

    public NetworkMonitorViewModel(MainViewModel main)
    {
        _main = main;
        _activeDnsName = main.ActiveDns;
        Title = "Network & DNS Optimization";
        Subtitle = "Inspect cumulative adapter byte totals; opt-in latency probe and IPv4 DNS settings";
    }

    partial void OnSelectedAdapterChanged(NetworkInterfaceInfo? value)
    {
        _main.Service.SelectedDnsAdapterId = value?.Id;
        _main.SyncDnsFromSystem();
        ActiveDnsName = value == null ? "Select an adapter" : _main.ActiveDns;
    }

    public override void OnNavigatedTo()
    {
        RefreshAdapters();

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
        if (SafetyMode.Enabled) { PingStatus = "External probes disabled in verification mode."; return; }
        PingStatus = "Pinging Google DNS (8.8.8.8)...";
        CurrentPingMs = await _netSvc.PingHostAsync("8.8.8.8");
        if (CurrentPingMs >= 0)
        {
            PingStatus = $"Latency: {CurrentPingMs} ms";
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
        if (SelectedAdapter == null) { PingStatus = "Select an adapter before applying IPv4 DNS."; return; }
        if (SafetyMode.Enabled) { PingStatus = "DNS changes disabled in verification mode."; return; }
        if (System.Windows.MessageBox.Show($"Set IPv4 DNS to {dns.Ip} on {SelectedAdapter.Name} ({SelectedAdapter.Id})? IPv6 is unchanged. This does not enable DNS encryption.", "Confirm adapter and DNS", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning, System.Windows.MessageBoxResult.No) != System.Windows.MessageBoxResult.Yes) return;
        _main.Service.SelectedDnsAdapterId = SelectedAdapter.Id;
        _main.SelectDnsCommand.Execute(dns);
        _main.LoadUndoHistory();
        ActiveDnsName = _main.Service.CurrentDns;
        PingStatus = _main.Service.LogStream.LastOrDefault() ?? "DNS operation finished.";
    }

    [RelayCommand]
    public void FlushDns()
    {
        try
        {
            SafetyMode.RequireMutationAllowed();
            var result = _main.Service.RunProcessPublic("ipconfig", "/flushdns", 3000);
            PingStatus = result.code == 0 ? "DNS cache flush completed." : $"DNS flush failed ({result.code}): {result.stderr}";
            _main.Log(PingStatus, result.code == 0 ? "success" : "warning");
        }
        catch (Exception ex)
        {
            _main.Log($"Failed to flush DNS: {ex.Message}", "error");
        }
    }
}
