using System.IO;
using System.Windows;
using System.Windows.Media;
using WinCareDesktop.Services;
using Xunit;

namespace WinCareDesktop.Tests;

public class FlightDeckCompleteImprovementsTests
{
    private static double Contrast(Color a, Color b)
    {
        static double Lum(Color c)
        {
            static double Ch(byte v)
            {
                var s = v / 255.0;
                return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Ch(c.R) + 0.7152 * Ch(c.G) + 0.0722 * Ch(c.B);
        }
        var l1 = Lum(a);
        var l2 = Lum(b);
        return (Math.Max(l1, l2) + 0.05) / (Math.Min(l1, l2) + 0.05);
    }

    [Theory]
    [InlineData("TextPrimary", "Surface", 15.0)]
    [InlineData("TextSecondary", "Surface", 10.0)]
    [InlineData("TextTertiary", "Surface", 5.5)]
    [InlineData("TextTertiary", "BgBrush", 6.0)]
    [InlineData("Primary", "Surface", 9.0)]
    [InlineData("Primary", "PrimaryLight", 7.0)]
    [InlineData("Success", "Surface", 8.0)]
    [InlineData("Warning", "Surface", 9.0)]
    [InlineData("Error", "Surface", 5.5)]
    public void Full_obsidian_dark_deck_palette_exceeds_wcag_aa_contrast(string fgKey, string bgKey, double minRatio)
    {
        UiTestHost.Run(() =>
        {
            var dict = new ResourceDictionary();
            ThemePaletteService.ApplyPalette(darkDeck: true, dict);

            var fg = Assert.IsType<SolidColorBrush>(dict[fgKey]);
            var bg = Assert.IsType<SolidColorBrush>(dict[bgKey]);
            var ratio = Contrast(fg.Color, bg.Color);
            Assert.True(ratio >= minRatio, $"Dark Deck {fgKey} on {bgKey} is {ratio:F2}:1 (expected >= {minRatio:F1}:1)");

            var btnBg = Assert.IsType<SolidColorBrush>(dict["PrimaryButtonBg"]);
            Assert.True(Contrast(Colors.White, btnBg.Color) >= 4.5);

            // Switching back to Hybrid restores white surface
            ThemePaletteService.ApplyPalette(darkDeck: false, dict);
            Assert.Equal(Colors.White, Assert.IsType<SolidColorBrush>(dict["Surface"]).Color);
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RescueOptionsStore_persists_dark_deck_preference_cleanly(bool darkDeck)
    {
        var path = Path.Combine(Path.GetTempPath(), $"darkdeck-opt-{Guid.NewGuid()}.json");
        try
        {
            var store = new RescueOptionsStore(path);
            store.Save(new RescueOptions(RefreshSeconds: 15, ReducedMotion: true, LocalDiagnostics: false, DarkDeck: darkDeck));
            Assert.Equal(darkDeck, store.Load().Value.DarkDeck);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Theory]
    [InlineData(false, true, true, "Warn", true, "FIREWALL", SecurityFindingLevel.Warning)]
    [InlineData(true, false, true, "Warn", true, "FIREWALL", SecurityFindingLevel.Warning)]
    [InlineData(true, true, false, "Warn", true, "UAC", SecurityFindingLevel.Warning)]
    [InlineData(true, true, true, "Off", true, "SMARTSCREEN", SecurityFindingLevel.Warning)]
    [InlineData(true, true, true, "Warn", false, "SECUREBOOT", SecurityFindingLevel.Review)]
    [InlineData(true, true, true, "Warn", true, "FIREWALL", SecurityFindingLevel.Clean)]
    [InlineData(true, true, true, "Block", true, "SMARTSCREEN", SecurityFindingLevel.Clean)]
    public void Os_security_posture_evaluator_flags_disabled_firewall_uac_smartscreen_and_secureboot(
        bool fwPriv, bool fwPub, bool uac, string smartScreen, bool secureBoot,
        string targetCategory, SecurityFindingLevel expectedLevel)
    {
        var posture = new SecurityThreatAuditService.OsSecurityPosture(fwPriv, fwPub, uac, smartScreen, secureBoot);
        var results = SecurityThreatAuditService.EvaluateSecurityPosture(posture);
        var match = Assert.Single(results, r => r.Category == targetCategory);
        Assert.Equal(expectedLevel, match.Level);
    }

    [Fact]
    public async Task DuplicateFileFinder_detects_exact_sha256_matches_and_ignores_same_size_non_matches()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dup-test-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        try
        {
            var payloadA = new byte[64 * 1024];
            var payloadB = new byte[64 * 1024];
            for (var i = 0; i < payloadA.Length; i++)
            {
                payloadA[i] = (byte)(i % 251);
                payloadB[i] = (byte)(i % 251);
            }
            // Same size & same first 16 KB header, but differs in byte 50,000!
            payloadB[50_000] ^= 0xFF;

            await File.WriteAllBytesAsync(Path.Combine(dir, "installer-copy1.bin"), payloadA);
            await File.WriteAllBytesAsync(Path.Combine(dir, "installer-copy2.bin"), payloadA);
            await File.WriteAllBytesAsync(Path.Combine(dir, "different-tail.bin"), payloadB);
            await File.WriteAllBytesAsync(Path.Combine(dir, "tiny.txt"), new byte[] { 1, 2, 3 });

            var svc = new DuplicateFileFinderService();
            var groups = await svc.FindDuplicatesAsync(dir, minFileBytes: 1024);

            var group = Assert.Single(groups);
            Assert.Equal(2, group.Count);
            Assert.Equal(payloadA.Length, group.FileSizeBytes);
            Assert.Equal(payloadA.Length, group.ReclaimableBytes);
            Assert.DoesNotContain(group.Paths, p => p.EndsWith("different-tail.bin", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task DuplicateFileFinder_blocks_scanning_inside_windows_directory()
    {
        var winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (string.IsNullOrWhiteSpace(winDir)) return;
        var svc = new DuplicateFileFinderService();
        await Assert.ThrowsAsync<IOException>(() => svc.FindDuplicatesAsync(winDir, minFileBytes: 1024));
    }

    [Fact]
    public void DefenderViewModel_open_startup_from_audit_invokes_navigation_callback()
    {
        var navigated = false;
        var vm = new WinCareDesktop.ViewModels.DefenderViewModel { NavigateToStartupApps = () => navigated = true };
        vm.OpenStartupFromAuditCommand.Execute(null);
        Assert.True(navigated);
    }

    [Fact]
    public void DashboardViewModel_cpu_display_formats_sampled_and_unsampled_states()
    {
        UiTestHost.Run(() =>
        {
            var mainVm = new WinCareDesktop.ViewModels.MainViewModel();
            mainVm.CpuPercent = -1;
            Assert.Equal("Not sampled", mainVm.DashboardVM.CpuDisplay);

            mainVm.CpuPercent = 27.4;
            Assert.Equal("27%", mainVm.DashboardVM.CpuDisplay);
        });
    }

    [Fact]
    public void DuplicateFileGroup_exposes_extra_path_and_reclaimable_bytes_accurately()
    {
        var group = new DuplicateFileGroup(
            "ABCDEF0123456789",
            10 * 1024 * 1024L,
            new[] { @"C:\Users\Alice\Downloads\setup.iso", @"C:\Users\Alice\Desktop\setup-copy.iso", @"C:\Users\Alice\Documents\setup-backup.iso" });

        Assert.Equal(3, group.Count);
        Assert.Equal(20 * 1024 * 1024L, group.ReclaimableBytes);
        Assert.Equal("setup.iso", group.PrimaryFileName);
        Assert.Equal(@"C:\Users\Alice\Desktop\setup-copy.iso", group.ExtraFilePath);
        Assert.Contains("setup-backup.iso", group.DuplicatePathsText);
    }
}
