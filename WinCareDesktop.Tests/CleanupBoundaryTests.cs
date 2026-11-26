using System.IO;
using WinCareDesktop.Services;
using WinCareDesktop.Models;
using Xunit;
namespace WinCareDesktop.Tests;
public class CleanupBoundaryTests
{
    [Fact]
    public void Cleanup_rejects_foreign_target_and_only_deletes_preview_snapshot()
    {
        var root = Path.Combine(Path.GetTempPath(), "scope-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        var foreign = Path.Combine(Path.GetTempPath(), "foreign-" + Guid.NewGuid());
        File.WriteAllText(foreign, "keep");
        try
        {
            var svc = TestService.New();
            typeof(SystemOptimizerService).GetProperty("CleanupRoots")?.SetValue(svc, new[] { root });
            Assert.Equal(0, svc.CleanTempAndCache(new[] { new CleanupCandidate { Path = foreign, Bytes = 4 } }));
            Assert.True(File.Exists(foreign));
            var sub = Directory.CreateDirectory(Path.Combine(root, "sub")).FullName;
            File.WriteAllText(Path.Combine(sub, "old"), "old");
            var preview = svc.ScanTempForCleanup();
            File.WriteAllText(Path.Combine(sub, "new"), "keep");
            svc.CleanTempAndCache(preview);
            Assert.True(File.Exists(Path.Combine(sub, "new")));
        }
        finally { Directory.Delete(root, true); File.Delete(foreign); }
    }
}
