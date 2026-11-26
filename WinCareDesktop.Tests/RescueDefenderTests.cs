using WinCareDesktop.Services;
using Xunit;
namespace WinCareDesktop.Tests;
public class RescueDefenderTests
{
    [Theory]
    [InlineData("Normal", true, true, "Active")]
    [InlineData("Passive", true, true, "Passive")]
    [InlineData("EDR Block Mode", true, true, "EDR")]
    [InlineData("Normal", false, true, "Disabled")]
    [InlineData("", true, true, "Unknown")]
    public void Status_never_turns_passive_disabled_or_unknown_into_clean(string mode,bool antivirus,bool realtime,string expected)
    {
        var status = new DefenderReading(mode,antivirus,realtime,DateTimeOffset.UtcNow,"fixture");
        Assert.StartsWith(expected,status.ProtectionText);
        Assert.DoesNotContain("clean",status.ProtectionText,StringComparison.OrdinalIgnoreCase);
    }
    [Fact] public void Signature_time_is_not_a_scan_verdict()
    {
        var status=new DefenderReading("Normal",true,true,null,"unavailable");
        Assert.Contains("Unknown",status.SignatureText);
        Assert.False(RescueActions.CanStartScan); Assert.False(RescueActions.CanCloseProcess);
    }
    [Fact] public void Safe_mode_blocks_shell_before_injected_launch()
    {
        int calls=0; Assert.Throws<InvalidOperationException>(()=>RescueActions.OpenWindowsSecurity(()=>calls++)); Assert.Equal(0,calls);
    }
}
