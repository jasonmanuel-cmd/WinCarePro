using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinCareDesktop.Services;
using System.Windows;
namespace WinCareDesktop.ViewModels;
public partial class DefenderViewModel : ViewModelBase
{
    public DefenderViewModel() : this(new NativeDefenderActions()) { }
    public Func<CancellationToken,Task<DefenderReading>> Read {get;set;} = DefenderStatusReader.ReadAsync;
    public Func<bool> ConfirmOpen {get;set;} = ()=>MessageBox.Show("Open Windows Security? Opening does not start a scan. If you choose a scan there, Defender may quarantine or remove threats according to its policy. Remediation is not automatically reversible.","Open Windows Security",MessageBoxButton.YesNo,MessageBoxImage.Warning)==MessageBoxResult.Yes;
    public Action LaunchSecurity {get;set;} = ()=>RescueActions.OpenWindowsSecurity();
    [ObservableProperty] private string _protection = "Not read — no security verdict available.";
    [ObservableProperty] private string _signatures = "Signature freshness unknown.";
    [ObservableProperty] private string _status = "Status is read only when requested. No scan runs on launch or navigation.";
    [ObservableProperty] private bool _busy;
    private CancellationTokenSource? _cancel;
    public bool CanOpenSecurity => !SafetyMode.Enabled;
    [RelayCommand] public async Task RefreshAsync()
    {
        if(Busy)return;
        Busy=true; using var cts=new CancellationTokenSource(); _cancel=cts;
        Status="Reading Defender status…";
        try { var data=await Read(cts.Token); cts.Token.ThrowIfCancellationRequested(); _active=Active(data); Protection=data.ProtectionText; Signatures=data.SignatureText; Status=$"Status sampled {DateTimeOffset.Now:g}. This status read starts no scan and makes no scan-completion claim."; }
        catch(OperationCanceledException) { Status="Status read cancelled. Previous observations may be stale; no Defender scan was cancelled."; }
        catch(Exception) { _active=false; Protection="Unavailable or error — Defender status could not be read. This does not mean your PC is clean."; Signatures="Signature freshness unknown."; Status="Review Windows Security for your active provider, permissions or managed policy. No protection settings were changed."; }
        finally { _cancel=null; Busy=false; }
    }
    [RelayCommand] public void Cancel() => _cancel?.Cancel();
    [RelayCommand] public void OpenSecurity()
    {
        if(SafetyMode.Enabled) {Status="Windows Security handoff is disabled in safe mode.";return;}
        if(!ConfirmOpen()) {Status="Handoff cancelled; Windows Security was not opened.";return;}
        try { LaunchSecurity(); Status="Windows Security launch requested. Review status, scans and Protection history there; launch is not completion evidence."; }
        catch(Exception) {Status="Windows Security could not be opened. Open it manually from Start; no scan was initiated by WinCare.";}
    }
    public override void OnNavigatedFrom(){Cancel();StopMonitoring();}
}
