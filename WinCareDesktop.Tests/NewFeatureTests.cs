using System.IO;
using WinCareDesktop.Models;
using WinCareDesktop.Services;
using WinCareDesktop.ViewModels;
using Xunit;

namespace WinCareDesktop.Tests;

public class NewFeatureTests
{
    [Fact]
    public void Scheduler_service_saves_and_loads_configuration()
    {
        var svc = new SchedulerService();
        var config = new ScheduleConfig
        {
            IsEnabled = true,
            Frequency = "Weekly",
            TimeOfDay = "04:30",
            CleanTempFiles = true,
            EmptyRecycleBin = false,
            OptimizeRam = true,
            FlushDns = true
        };

        svc.SaveConfig(config);
        var loaded = svc.LoadConfig();

        Assert.True(loaded.IsEnabled);
        Assert.Equal("Weekly", loaded.Frequency);
        Assert.Equal("04:30", loaded.TimeOfDay);
        Assert.True(loaded.CleanTempFiles);
        Assert.False(loaded.EmptyRecycleBin);
        Assert.True(loaded.OptimizeRam);
        Assert.True(loaded.FlushDns);
    }

    [Fact]
    public async Task Disk_analyzer_scans_temp_directory()
    {
        var svc = new DiskAnalyzerService();
        var tempDir = Path.Combine(Path.GetTempPath(), "WinCareTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            File.WriteAllText(Path.Combine(tempDir, "sample.mp4"), new string('a', 5000));
            File.WriteAllText(Path.Combine(tempDir, "doc.pdf"), new string('b', 3000));

            var results = await svc.ScanDirectoryAsync(tempDir);

            Assert.Equal(2, results.Count);
            var video = results.First(x => x.Name == "sample.mp4");
            Assert.Equal("Video", video.Category);
            Assert.Equal(5000, video.Bytes);

            var doc = results.First(x => x.Name == "doc.pdf");
            Assert.Equal("Document", doc.Category);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Startup_manager_reads_entries()
    {
        var svc = new StartupManagerService();
        var entries = svc.GetStartupEntries();
        Assert.NotNull(entries);
    }

    [Fact]
    public void Network_monitor_reads_interfaces()
    {
        var svc = new NetworkMonitorService();
        var ifaces = svc.GetActiveInterfaces();
        Assert.NotNull(ifaces);
    }

    [Fact]
    public void Selectable_app_model_wraps_installed_program()
    {
        var prog = new InstalledProgram
        {
            Name = "Test Application",
            Publisher = "Test Vendor",
            SizeBytes = 1024 * 1024 * 50,
            Kind = UninstallKind.Classic
        };

        var app = new SelectableApp(prog);
        Assert.Equal("Test Application", app.Name);
        Assert.Equal("Test Vendor", app.Publisher);
        Assert.False(app.IsSelected);
        app.IsSelected = true;
        Assert.True(app.IsSelected);
    }
}
