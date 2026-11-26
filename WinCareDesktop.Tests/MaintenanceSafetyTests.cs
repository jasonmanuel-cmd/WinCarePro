using System.IO;
using WinCareDesktop.ViewModels;
using WinCareDesktop.Models;
using Xunit;
namespace WinCareDesktop.Tests;
public class MaintenanceSafetyTests
{
    [Fact]
    public async Task Maintenance_decline_preserves_preview_files()
    {
        var property = typeof(MainViewModel).GetProperty("ConfirmCleanup");
        Assert.NotNull(property);
        MainViewModel vm = null!;
        UiTestHost.Run(() => vm = new MainViewModel());
        var root = Path.Combine(Path.GetTempPath(), "maintenance-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, "keep.txt"); File.WriteAllText(file, "keep");
        try
        {
            vm.Service.CleanupRoots = new[] { root };
            bool requested = false;
            property.SetValue(vm, (Func<IReadOnlyCollection<CleanupCandidate>, bool>)(items => { requested = true; return false; }));
            await vm.RunOptimizationCommand.ExecuteAsync(vm.Service.ScanTempForCleanup());
            Assert.True(requested);
            Assert.True(File.Exists(file));
            Assert.Contains("cancelled", vm.StatusMessage, StringComparison.OrdinalIgnoreCase);
        }
        finally { Directory.Delete(root, true); }
    }
}
