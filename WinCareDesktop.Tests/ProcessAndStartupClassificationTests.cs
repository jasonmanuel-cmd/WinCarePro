using System.Diagnostics;
using WinCareDesktop.Services;
using Xunit;

namespace WinCareDesktop.Tests;

public class ProcessAndStartupClassificationTests
{
    [Theory]
    [InlineData("System", "Windows System — keep running")]
    [InlineData("Idle", "Windows System — keep running")]
    [InlineData("Registry", "Windows System — keep running")]
    [InlineData("smss", "Windows System — keep running")]
    [InlineData("csrss", "Windows System — keep running")]
    [InlineData("wininit", "Windows System — keep running")]
    [InlineData("services", "Windows System — keep running")]
    [InlineData("lsass", "Windows System — keep running")]
    [InlineData("svchost", "Windows System — keep running")]
    [InlineData("SVCHOST", "Windows System — keep running")]
    [InlineData("  dwm  ", "Windows System — keep running")]
    [InlineData("winlogon", "Windows System — keep running")]
    [InlineData("fontdrvhost", "Windows System — keep running")]
    [InlineData("sihost", "Windows System — keep running")]
    [InlineData("taskhostw", "Windows System — keep running")]
    [InlineData("explorer", "Windows System — keep running")]
    [InlineData("ctfmon", "Windows System — keep running")]
    [InlineData("conhost", "Windows System — keep running")]
    [InlineData("dllhost", "Windows System — keep running")]
    [InlineData("audiodg", "Windows System — keep running")]
    [InlineData("spoolsv", "Windows System — keep running")]
    [InlineData("WmiPrvSE", "Windows System — keep running")]
    [InlineData("SearchIndexer", "Windows System — keep running")]
    [InlineData("SearchHost", "Windows System — keep running")]
    [InlineData("StartMenuExperienceHost", "Windows System — keep running")]
    [InlineData("ShellExperienceHost", "Windows System — keep running")]
    [InlineData("RuntimeBroker", "Windows System — keep running")]
    [InlineData("TextInputHost", "Windows System — keep running")]
    [InlineData("MsMpEng", "Windows Security — keep running")]
    [InlineData("MpDefenderCoreService", "Windows Security — keep running")]
    [InlineData("NisSrv", "Windows Security — keep running")]
    [InlineData("SecurityHealthService", "Windows Security — keep running")]
    [InlineData("SecurityHealthSystray", "Windows Security — keep running")]
    [InlineData("smartscreen", "Windows Security — keep running")]
    [InlineData("chrome", "Browser / WebView — close unused tabs")]
    [InlineData("CHROME", "Browser / WebView — close unused tabs")]
    [InlineData("msedge", "Browser / WebView — close unused tabs")]
    [InlineData("msedgewebview2", "Browser / WebView — close unused tabs")]
    [InlineData("firefox", "Browser / WebView — close unused tabs")]
    [InlineData("brave", "Browser / WebView — close unused tabs")]
    [InlineData("opera", "Browser / WebView — close unused tabs")]
    [InlineData("vivaldi", "Browser / WebView — close unused tabs")]
    [InlineData("iexplore", "Browser / WebView — close unused tabs")]
    [InlineData("Discord", "Communication / Chat")]
    [InlineData("Teams", "Communication / Chat")]
    [InlineData("ms-teams", "Communication / Chat")]
    [InlineData("slack", "Communication / Chat")]
    [InlineData("Zoom", "Communication / Chat")]
    [InlineData("Telegram", "Communication / Chat")]
    [InlineData("WhatsApp", "Communication / Chat")]
    [InlineData("Skype", "Communication / Chat")]
    [InlineData("steam", "Game / Launcher")]
    [InlineData("steamwebhelper", "Game / Launcher")]
    [InlineData("EpicGamesLauncher", "Game / Launcher")]
    [InlineData("Battle.net", "Game / Launcher")]
    [InlineData("RiotClientServices", "Game / Launcher")]
    [InlineData("GalaxyClient", "Game / Launcher")]
    [InlineData("EA Desktop", "Game / Launcher")]
    [InlineData("vlc", "Media / Audio")]
    [InlineData("Spotify", "Media / Audio")]
    [InlineData("wmplayer", "Media / Audio")]
    [InlineData("Music.UI", "Media / Audio")]
    [InlineData("Video.UI", "Media / Audio")]
    [InlineData("obs64", "Media / Audio")]
    [InlineData("audacity", "Media / Audio")]
    [InlineData("Code", "Developer / Shell")]
    [InlineData("devenv", "Developer / Shell")]
    [InlineData("dotnet", "Developer / Shell")]
    [InlineData("node", "Developer / Shell")]
    [InlineData("python", "Developer / Shell")]
    [InlineData("pythonw", "Developer / Shell")]
    [InlineData("java", "Developer / Shell")]
    [InlineData("javaw", "Developer / Shell")]
    [InlineData("powershell", "Developer / Shell")]
    [InlineData("pwsh", "Developer / Shell")]
    [InlineData("WindowsTerminal", "Developer / Shell")]
    [InlineData("CustomVendorApp", "User or background process")]
    [InlineData("notepad", "User or background process")]
    [InlineData("", "Unknown role")]
    [InlineData("   ", "Unknown role")]
    [InlineData(null, "Unknown role")]
    public void Process_role_classifier_labels_known_and_unknown_processes_accurately(string? name, string expectedRole)
    {
        Assert.Equal(expectedRole, MemoryRow.ClassifyProcessRole(name));
    }

    [Theory]
    [InlineData("\"C:\\Users\\User\\AppData\\Local\\Discord\\Update.exe\" --processStart Discord.exe", "High")]
    [InlineData("\"C:\\Program Files (x86)\\Steam\\steam.exe\" -silent", "High")]
    [InlineData("\"C:\\Users\\User\\AppData\\Roaming\\Spotify\\Spotify.exe\" --minimized", "High")]
    [InlineData("\"C:\\Program Files\\WindowsApps\\MSTeams\\ms-teams.exe\"", "High")]
    [InlineData("\"C:\\Users\\User\\AppData\\Local\\slack\\slack.exe\" --startup", "High")]
    [InlineData("\"C:\\Users\\User\\AppData\\Roaming\\Zoom\\bin\\Zoom.exe\"", "High")]
    [InlineData("\"C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe\" --no-startup-window", "High")]
    [InlineData("\"C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe\" --no-startup-window", "High")]
    [InlineData("\"C:\\Program Files\\BraveSoftware\\Brave-Browser\\Application\\brave.exe\"", "High")]
    [InlineData("\"C:\\Program Files\\Mozilla Firefox\\firefox.exe\"", "High")]
    [InlineData("\"C:\\Program Files (x86)\\Epic Games\\Launcher\\Portal\\Binaries\\Win64\\EpicGamesLauncher.exe\" -silent", "High")]
    [InlineData("\"C:\\Riot Games\\Riot Client\\RiotClientServices.exe\" --launch-background-mode", "High")]
    [InlineData("\"C:\\Program Files (x86)\\Battle.net\\Battle.net.exe\"", "High")]
    [InlineData("\"C:\\Program Files (x86)\\Overwolf\\Overwolf.exe\" -silent", "High")]
    [InlineData("\"C:\\Program Files\\Adobe\\Adobe Creative Cloud\\ACC\\Creative Cloud.exe\"", "High")]
    [InlineData("\"C:\\Program Files\\Docker\\Docker\\Docker Desktop.exe\"", "High")]
    [InlineData("\"C:\\Program Files\\Skype\\Phone\\Skype.exe\"", "High")]
    [InlineData("Cortana.exe", "High")]
    [InlineData("\"C:\\Program Files\\ASUS\\Armoury Crate\\ArmouryCrate.exe\"", "High")]
    [InlineData("\"C:\\Program Files\\Corsair\\CORSAIR iCUE 5 Software\\iCUE.exe\" --autorun", "High")]
    [InlineData("\"C:\\Program Files (x86)\\Razer\\Synapse3\\WPFUI\\Framework\\Razer Synapse 3 Host\\Razer Synapse 3.exe\"", "High")]
    [InlineData("\"C:\\Program Files\\LGHUB\\lghub.exe\" --background", "High")]
    [InlineData("C:\\Apps\\electron-shell\\app.exe", "High")]
    [InlineData("\"C:\\Users\\User\\AppData\\Local\\Microsoft\\OneDrive\\OneDrive.exe\" /background", "Medium")]
    [InlineData("\"C:\\Program Files (x86)\\Dropbox\\Client\\Dropbox.exe\" /systemstartup", "Medium")]
    [InlineData("\"C:\\Program Files\\Google\\Drive File Stream\\GoogleDriveFS.exe\" --startup_mode", "Medium")]
    [InlineData("\"C:\\Program Files\\Common Files\\Java\\Java Update\\jusched.exe\" -updater", "Medium")]
    [InlineData("\"C:\\Program Files\\Vendor\\BackgroundHelper.exe\"", "Medium")]
    [InlineData("\"C:\\Program Files\\Vendor\\CloudSyncDaemon.exe\"", "Medium")]
    [InlineData("\"C:\\Program Files\\Vendor\\AudioTray.exe\"", "Medium")]
    [InlineData("\"C:\\Program Files\\Vendor\\BackupAgent.exe\"", "Medium")]
    [InlineData("\"C:\\Program Files\\Realtek\\Audio\\HDA\\RtkAudUService64.exe\"", "Low")]
    [InlineData("\"C:\\Windows\\System32\\SecurityHealthSystray.exe\"", "Low")]
    [InlineData("\"C:\\Tools\\EarTrumpet.exe\"", "Low")]
    [InlineData("\"C:\\Tools\\f.lux\\flux.exe\" /noshow", "Low")]
    [InlineData("\"C:\\Tools\\AutoHotkey\\AutoHotkey.exe\" script.ahk", "Low")]
    [InlineData("", "Low")]
    public void Startup_impact_estimator_classifies_real_world_commands(string command, string expectedImpact)
    {
        Assert.Equal(expectedImpact, StartupManagerService.EstimateImpact(command));
    }

    public static IEnumerable<object[]> ProcessorCoreCounts() =>
        new[] { 1, 2, 4, 6, 8, 12, 16, 24, 32, 64 }.Select(c => new object[] { c });

    [Theory]
    [MemberData(nameof(ProcessorCoreCounts))]
    public void Memory_sampler_normalizes_cpu_across_logical_processor_counts(int cores)
    {
        long hz = Stopwatch.Frequency;
        // 500ms of CPU time over 1.0s wall interval = (50 / cores)% total CPU
        var before = new[] { new ProcessSample(42, 1000, "worker", 256L * 1048576L, 1000.0, hz) };
        var after = new[] { new ProcessSample(42, 1000, "worker", 512L * 1048576L, 1500.0, 2 * hz) };

        var row = Assert.Single(MemorySampler.Compare(before, after, TimeSpan.FromSeconds(1), cores));
        Assert.Equal(512.0, row.MemoryMb, 3);
        Assert.NotNull(row.CpuPercent);
        Assert.Equal(Math.Clamp(50.0 / cores, 0, 100), row.CpuPercent!.Value, 3);
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(-500, 4)]
    [InlineData(1000, 0)]
    [InlineData(1000, -2)]
    public void Memory_sampler_returns_null_cpu_when_interval_or_processors_invalid(int intervalMs, int processors)
    {
        var before = new[] { new ProcessSample(10, 50, "app", 1048576, 100, 0) };
        var after = new[] { new ProcessSample(10, 50, "app", 1048576, 200, 0) };
        var row = Assert.Single(MemorySampler.Compare(before, after, TimeSpan.FromMilliseconds(intervalMs), processors));
        Assert.Null(row.CpuPercent);
        Assert.Equal("Unknown", row.CpuText);
    }
}
