using System.IO;
using WinCareDesktop.Services;
using Xunit;

namespace WinCareDesktop.Tests;

public class CompletionSafetyTests
{
    [Fact]
    public void Disk_declined_confirmation_keeps_exact_target()
    {
        var path = Path.Combine(Path.GetTempPath(), "disk-" + Guid.NewGuid() + ".txt");
        File.WriteAllText(path, "keep");
        try
        {
            var ctor = typeof(WinCareDesktop.ViewModels.DiskAnalyzerViewModel).GetConstructor(new[] { typeof(Func<string, bool>), typeof(Action<string>) });
            string? confirmed = null;
            Func<string, bool> deny = p => { confirmed = p; return false; };
            var vm = ctor == null ? new WinCareDesktop.ViewModels.DiskAnalyzerViewModel() : (WinCareDesktop.ViewModels.DiskAnalyzerViewModel)ctor.Invoke(new object[] { deny, (Action<string>)(_ => throw new Exception("Must not delete")) });
            vm.DeleteItem(new DiskItem { FullPath = path, Name = "test" });
            Assert.True(File.Exists(path), "A declined or missing confirmation must never delete the file");
            Assert.Equal(path, confirmed);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Disk_rejects_protected_paths_before_confirmation()
    {
        var called = false;
        var vm = new WinCareDesktop.ViewModels.DiskAnalyzerViewModel(_ => { called = true; return true; }, _ => { });
        vm.DeleteItem(new DiskItem { FullPath = Environment.GetFolderPath(Environment.SpecialFolder.Windows), IsDirectory = true });
        Assert.False(called);
        Assert.Contains("protected", vm.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Cleanup_counts_only_successful_file_deletions_and_reports_errors()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cleanup-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        var locked = Path.Combine(dir, "locked.txt");
        File.WriteAllText(locked, "locked");
        File.WriteAllText(Path.Combine(dir, "free.txt"), "free");
        try
        {
            var svc = TestService.New();
            svc.CleanupRoots = new[] { dir };
            var preview = svc.ScanTempForCleanup();
            using var handle = new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None);
            var bytes = svc.CleanTempAndCache(preview);
            Assert.Equal(4, bytes);
            Assert.Contains(svc.LogStream, x => x.Contains("locked.txt"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Legacy_enabled_schedule_requires_fresh_session_consent()
    {
        var path = Path.Combine(Path.GetTempPath(), "legacy-" + Guid.NewGuid() + ".json");
        File.WriteAllText(path, "{\"IsEnabled\":true,\"Frequency\":\"Daily\",\"EmptyRecycleBin\":true}");
        try { Assert.False(new SchedulerService(path).LoadConfig().IsEnabled); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Disk_refuses_target_changed_during_confirmation()
    {
        var path = Path.Combine(Path.GetTempPath(), "changed-" + Guid.NewGuid());
        File.WriteAllText(path, "original");
        bool removed = false;
        try
        {
            var vm = new WinCareDesktop.ViewModels.DiskAnalyzerViewModel(_ => { File.WriteAllText(path, "changed contents"); return true; }, _ => removed = true);
            vm.DeleteItem(new DiskItem { FullPath = path });
            Assert.False(removed);
            Assert.Contains("changed", vm.StatusText, StringComparison.OrdinalIgnoreCase);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Disk_protects_ancestors_of_profile_not_only_profile_itself()
    {
        var ancestor = Path.GetDirectoryName(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))!;
        var error = Assert.Throws<IOException>(() => PathSafety.ValidateRemoval(ancestor));
        Assert.Contains("Protected", error.Message);
    }

    [Fact]
    public void Scheduler_supports_explicit_isolated_store()
    {
        Assert.NotNull(typeof(SchedulerService).GetConstructor(new[] { typeof(string) }));
    }
}
