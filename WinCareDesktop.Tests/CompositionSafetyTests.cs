using System.IO;
using System.Runtime.CompilerServices;
using WinCareDesktop.Services;
using Xunit;

namespace WinCareDesktop.Tests;
public static class SafeTestComposition
{
    [ModuleInitializer]
    public static void Initialize()
    {
        Environment.SetEnvironmentVariable("WINCARE_SAFE_MODE", "1");
        Environment.SetEnvironmentVariable("WINCARE_DATA_ROOT", Path.Combine(Path.GetTempPath(), "wincare-test-run-" + Guid.NewGuid()));
    }
}
public class CompositionSafetyTests
{
    [Fact]
    public void Safe_launch_diagnostics_write_only_to_isolated_data_root()
    {
        var dataDir = typeof(WinCareDesktop.App).GetProperty("DataDir", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null)!.ToString();
        Assert.Equal(Environment.GetEnvironmentVariable("WINCARE_DATA_ROOT"), dataDir);
        Assert.Empty(new SystemOptimizerService().CleanupRoots);
    }

    [Fact]
    public void Closing_window_stops_polling_and_scheduler()
    {
        UiTestHost.Run(() =>
        {
            var window = new MainWindow(); window.Show(); window.Close();
            var timer = (System.Windows.Threading.DispatcherTimer)typeof(MainWindow).GetField("_pollTimer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(window)!;
            Assert.False(timer.IsEnabled);
        });
    }

    [Fact]
    public void Default_history_store_is_confined_to_test_data_root()
    {
        Assert.StartsWith(Environment.GetEnvironmentVariable("WINCARE_DATA_ROOT")!, new HistoryStore().FilePath);
    }
}
