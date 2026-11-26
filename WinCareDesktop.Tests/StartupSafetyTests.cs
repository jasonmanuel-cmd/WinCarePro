using System.IO;
using WinCareDesktop.Services;
using Xunit;

namespace WinCareDesktop.Tests;

public class StartupSafetyTests
{
    [Fact]
    public void Startup_restore_refuses_destination_even_when_its_value_kind_is_unsupported()
    {
        var registry = new Dictionary<string, string> { ["User Registry|Disabled|Example"] = "original.exe" };
        var svc = new StartupManagerService(Path.Combine(Path.GetTempPath(), "startup-fixture-" + Guid.NewGuid()), registry);
        var exists = typeof(StartupManagerService).GetProperty("RunValueExists");
        Assert.NotNull(exists);
        exists.SetValue(svc, (Func<string, bool, string, bool>)((_, disabled, _) => !disabled));
        Assert.False(svc.ToggleStartupEntry(Assert.Single(svc.GetStartupEntries()), true));
        Assert.Equal("original.exe", Assert.Single(registry).Value);
    }

    [Fact]
    public void Startup_inventory_does_not_offer_desktop_metadata_as_a_program()
    {
        var dir = Path.Combine(Path.GetTempPath(), "startup-metadata-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "desktop.ini"), "fixture metadata");
            File.WriteAllText(Path.Combine(dir, "real.lnk"), "fixture shortcut");
            Assert.Equal("real", Assert.Single(new StartupManagerService(dir).GetStartupEntries()).Name);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Registry_disable_preserves_command_across_service_restart()
    {
        var dir = Path.Combine(Path.GetTempPath(), "startup-" + Guid.NewGuid());
        var registry = new Dictionary<string, string> { ["User Registry|Run|Example"] = "\"C:\\Example\\app.exe\" --start" };
        var ctor = typeof(StartupManagerService).GetConstructor(new[] { typeof(string), typeof(IDictionary<string, string>) });
        Assert.NotNull(ctor);
        var svc = (StartupManagerService)ctor.Invoke(new object[] { dir, registry });
        var entry = Assert.Single(svc.GetStartupEntries());
        Assert.True(svc.ToggleStartupEntry(entry, false));
        svc = (StartupManagerService)ctor.Invoke(new object[] { dir, registry });
        entry = Assert.Single(svc.GetStartupEntries());
        Assert.False(entry.IsEnabled);
        Assert.Equal("\"C:\\Example\\app.exe\" --start", entry.Command);
        Assert.True(svc.ToggleStartupEntry(entry, true));
        Assert.True(Assert.Single(svc.GetStartupEntries()).IsEnabled);
    }

    [Fact]
    public void Disabled_folder_entries_survive_reload_and_never_overwrite_restore_target()
    {
        var dir = Path.Combine(Path.GetTempPath(), "startup-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "test.lnk.disabled"), "disabled");
            var ctor = typeof(StartupManagerService).GetConstructor(new[] { typeof(string) });
            Assert.NotNull(ctor);
            var svc = (StartupManagerService)ctor.Invoke(new object[] { dir });
            var entry = Assert.Single(svc.GetStartupEntries());
            Assert.False(entry.IsEnabled);
            File.WriteAllText(Path.Combine(dir, "test.lnk"), "keep");
            Assert.False(svc.ToggleStartupEntry(entry, true));
            Assert.Equal("keep", File.ReadAllText(Path.Combine(dir, "test.lnk")));
            File.Delete(Path.Combine(dir, "test.lnk"));
            Assert.True(svc.ToggleStartupEntry(entry, true));
            Assert.True(Assert.Single(svc.GetStartupEntries()).IsEnabled);
        }
        finally { Directory.Delete(dir, true); }
    }
}
