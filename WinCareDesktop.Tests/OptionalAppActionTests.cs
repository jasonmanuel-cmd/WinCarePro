using WinCareDesktop.Services;
using WinCareDesktop.ViewModels;
using Xunit;
namespace WinCareDesktop.Tests;
public class OptionalAppActionTests
{
    internal static OptionalAppIdentity Identity=new(987654321,123,"C:\\Program Files\\VideoLAN\\VLC\\vlc.exe","fixture-sid",5,1234,"VLC media player",true,false);
    [Fact] public void Only_trusted_named_optional_app_in_same_user_session_is_eligible()
    {
        Assert.True(OptionalAppRules.Eligible(Identity,"fixture-sid",5));
        foreach(var bad in new[]{Identity with{Pid=0},Identity with{StartedUtcTicks=0},Identity with{Path=""},Identity with{OwnerSid="other"},Identity with{Session=6},Identity with{Window=0},Identity with{App="Windows Security"},Identity with{TrustedInstall=false},Identity with{ProtectedRole=true}})
            Assert.False(OptionalAppRules.Eligible(bad,"fixture-sid",5));
    }
    [Theory][InlineData("pid")][InlineData("start")][InlineData("path")][InlineData("owner")][InlineData("session")][InlineData("window")][InlineData("trust")][InlineData("role")]
    public async Task Changed_identity_after_confirmation_is_never_closed(string change)
    {
        var f=new Fake();var vm=Vm(f);await vm.ReviewAppsAsync();var row=Assert.Single(vm.OptionalApps);
        vm.ConfirmClose=_=>{f.Current=change switch {"pid"=>Identity with{Pid=1},"start"=>Identity with{StartedUtcTicks=124},"path"=>Identity with{Path="C:\\changed.exe"},"owner"=>Identity with{OwnerSid="other"},"session"=>Identity with{Session=6},"window"=>Identity with{Window=5678},"trust"=>Identity with{TrustedInstall=false},_=>Identity with{ProtectedRole=true}};return true;};
        await vm.CloseAppAsync(row);Assert.Equal(0,f.Closes);Assert.Contains("refused",vm.AppStatus);
    }
    [Fact] public async Task Confirmed_exact_fake_target_gets_one_graceful_request_and_audit()
    {
        var f=new Fake();var vm=Vm(f);var audit=new List<string>();vm.Audit=(a,o)=>audit.Add(a+o);await vm.ReviewAppsAsync();var row=Assert.Single(vm.OptionalApps);
        await vm.CloseAppAsync(row);await vm.CloseAppAsync(row);Assert.Equal(1,f.Closes);Assert.Equal(2,audit.Count);Assert.Empty(vm.OptionalApps);
    }
    [Fact] public async Task Decline_and_audit_error_prevent_close()
    {
        var f=new Fake();var vm=Vm(f);await vm.ReviewAppsAsync();vm.ConfirmClose=_=>false;await vm.CloseAppAsync(vm.OptionalApps[0]);Assert.Equal(0,f.Closes);
        await vm.ReviewAppsAsync();vm.ConfirmClose=_=>true;vm.Audit=(_,_)=>throw new IOException();await vm.CloseAppAsync(vm.OptionalApps[0]);Assert.Equal(0,f.Closes);
    }
    [Fact] public async Task Cancellation_during_close_audit_never_calls_adapter()
    {
        var f=new Fake();var vm=Vm(f);await vm.ReviewAppsAsync();
        vm.Audit=(_,_)=>vm.CancelAppReview();
        await vm.CloseAppAsync(Assert.Single(vm.OptionalApps));
        Assert.Equal(0,f.Closes);Assert.Contains("preparation cancelled",vm.AppStatus);
    }
    [Fact] public async Task Safe_native_close_refuses_before_process_lookup_even_for_fake_pid()
    {await Assert.ThrowsAsync<InvalidOperationException>(()=>new NativeOptionalAppActions().Close(Identity,default));}
    [Fact] public async Task Unknown_apps_are_not_offered_as_close_targets()
    {var f=new Fake{Current=Identity with{App="unknown"}};var vm=Vm(f);await vm.ReviewAppsAsync();Assert.Empty(vm.OptionalApps);}
    private static MemoryViewModel Vm(Fake f)=>new(f){ConfirmClose=_=>true,Audit=(_,_)=>{}};
    private sealed class Fake:IOptionalAppActions
    {
        public bool ActionsAllowed=>true;public string UserSid=>"fixture-sid";public int Session=>5;public int Closes;public OptionalAppIdentity Current=Identity;
        public Task<IReadOnlyList<OptionalAppCandidate>> Read(CancellationToken token)=>Task.FromResult<IReadOnlyList<OptionalAppCandidate>>(new[]{new OptionalAppCandidate(Current,"fixture")});
        public Task<OptionalAppIdentity?> Revalidate(OptionalAppIdentity identity,CancellationToken token)=>Task.FromResult<OptionalAppIdentity?>(Current);
        public Task<OptionalAppCloseResult> Close(OptionalAppIdentity identity,CancellationToken token){Closes++;return Task.FromResult(new OptionalAppCloseResult(true,false,"Graceful request sent; exit not verified (fixture)."));}
    }
    [Fact] public void Memory_has_individual_close_command()=>Assert.NotNull(typeof(MemoryViewModel).GetProperty("CloseAppCommand"));
}
