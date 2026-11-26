using System.Windows;
using System.Windows.Controls;
using WinCareDesktop.Services;
using WinCareDesktop.ViewModels;
namespace RenderShots;
internal static partial class Program
{
    private static void CaptureActionFixtures(Window window,MainViewModel main,string output)
    {
        void Wait(Task task){var end=DateTime.UtcNow.AddSeconds(10);while(!task.IsCompleted && DateTime.UtcNow<end){Pump();Thread.Sleep(10);}task.GetAwaiter().GetResult();}
        void Capture(string name,string button)
        {
            window.UpdateLayout();Pump();
            All(window).OfType<Button>().First(b=>b.Content as string==button).BringIntoView();
            window.UpdateLayout();Pump();Shot(Client(window),output,"actions-"+name);
        }
        var fake=new FixtureDefender();
        var defender=new DefenderViewModel(fake){Subtitle="SYNTHETIC action fixture — no host scan or remediation",ConfirmScan=_=>true,Audit=(_,_)=>{}};
        main.Navigate(main.NavigationItems.Single(n=>n.Title=="Defender"));
        main.CurrentView=defender;Wait(defender.RefreshAsync());Capture("defender-enabled","Quick scan…");
        Wait(defender.QuickScanAsync());Capture("defender-requested","Refresh scan lifecycle");
        fake.End=0;Wait(defender.MonitorAsync());Capture("defender-running","Refresh scan lifecycle");
        fake.End=1001;Wait(defender.MonitorAsync());Capture("defender-completed","Refresh scan lifecycle");
        Wait(defender.ReadDetectionsAsync());Capture("defender-detections","Open Windows Security…");
        window.Width=980;window.Height=650;Capture("minimum-defender","Refresh scan lifecycle");
        window.Width=1180;window.Height=760;
        var apps=new MemoryViewModel(new FixtureApps()){Subtitle="SYNTHETIC app fixture — no live target or close",ConfirmClose=_=>true,Audit=(_,_)=>{}};
        main.Navigate(main.NavigationItems.Single(n=>n.Title=="Memory & Apps"));
        main.CurrentView=apps;Wait(apps.ReviewAppsAsync());Capture("app-enabled","Close this app…");
        window.Width=980;window.Height=650;Capture("minimum-app","Close this app…");
        window.Width=1180;window.Height=760;
        Wait(apps.CloseAppAsync(apps.OptionalApps[0]));Capture("app-requested","Review eligible optional apps");
        var disabled=new MemoryViewModel{Subtitle="SYNTHETIC identity • native safe-mode guard stays enabled",OptionalApps=new[]{new OptionalAppCandidate(FixtureApps.Identity,"Synthetic candidate; safe-mode close disabled.")}};
        main.CurrentView=disabled;Capture("app-safe-disabled","Close this app…");
        main.Navigate(main.NavigationItems.Single(n=>n.Title=="Defender"));
        main.CurrentView=new DefenderViewModel{Subtitle="Native safe-mode scan guard • no host scan"};Capture("defender-safe-disabled","Quick scan…");
    }
    private sealed class FixtureDefender:IDefenderActions
    {
        public bool ActionsAllowed=>true;public string User=>"fixture";public int End;private DateTimeOffset? _start;
        public Task<DefenderReading> Status(CancellationToken token)=>Task.FromResult(new DefenderReading("Normal",true,true,DateTimeOffset.UtcNow,"fixture"));
        public Task<RescueCommandResult> Start(RescueScanKind kind,CancellationToken preparationToken=default){preparationToken.ThrowIfCancellationRequested();_start=DateTimeOffset.UtcNow;return Task.FromResult(new RescueCommandResult(0,""));}
        public Task<DefenderEventBatch> Events(CancellationToken token)
        {
            const string scan="11111111-1111-1111-1111-111111111111";var events=new List<DefenderScanEvent>();
            if(_start is {} at){events.Add(new(11,at,1000,scan,"Quick Scan","fixture","Microsoft-Windows-Windows Defender"));if(End!=0)events.Add(new(12,at.AddMilliseconds(1),End,scan,"Quick Scan","fixture","Microsoft-Windows-Windows Defender"));}
            return Task.FromResult(new DefenderEventBatch(_start is null?10:12,events,true));
        }
        public Task<string> Detections(CancellationToken token)=>Task.FromResult("Synthetic history: threat ID 123 • provider status 1 • action success false. Incomplete fixture; review Protection history. No host detections were read.");
    }
    private sealed class FixtureApps:IOptionalAppActions
    {
        public static readonly OptionalAppIdentity Identity=new(987654321,DateTime.UtcNow.Ticks,"C:\\Synthetic\\vlc.exe","fixture",5,1234,"VLC media player",true,false);
        public bool ActionsAllowed=>true;public string UserSid=>"fixture";public int Session=>5;
        public Task<IReadOnlyList<OptionalAppCandidate>> Read(CancellationToken token)=>Task.FromResult<IReadOnlyList<OptionalAppCandidate>>(new[]{new OptionalAppCandidate(Identity,"Fake trusted identity; save recordings first.")});
        public Task<OptionalAppIdentity?> Revalidate(OptionalAppIdentity identity,CancellationToken token)=>Task.FromResult<OptionalAppIdentity?>(Identity);
        public Task<OptionalAppCloseResult> Close(OptionalAppIdentity identity,CancellationToken token)=>Task.FromResult(new OptionalAppCloseResult(true,false,"Fixture close request sent; exit not verified. No live process was touched. No force fallback."));
    }
}
