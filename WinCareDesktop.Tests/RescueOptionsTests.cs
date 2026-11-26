using System.IO;
using WinCareDesktop.Services;
using Xunit;

namespace WinCareDesktop.Tests;
public class RescueOptionsTests
{
    [Fact]
    public void Save_roundtrips_and_reset_only_changes_app_preferences()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()); Directory.CreateDirectory(dir);
        var path = Path.Combine(dir,"options.json"); var store = new RescueOptionsStore(path);
        File.WriteAllText(Path.Combine(dir,"schedule.json"), "untouched");
        store.Save(new(RefreshSeconds:60)); Assert.Equal(60,store.Load().Value.RefreshSeconds);
        store.Save(new()); Assert.Equal(new RescueOptions(),store.Load().Value);
        Assert.Equal("untouched",File.ReadAllText(Path.Combine(dir,"schedule.json")));
        Assert.Single(Directory.GetFiles(dir,"options*"));
    }
    [Fact]
    public void Save_failure_preserves_existing_bytes_and_removes_staging()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()); Directory.CreateDirectory(dir);
        var path = Path.Combine(dir,"options.json"); File.WriteAllText(path,"original");
        using(var held = new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read))
            Assert.ThrowsAny<Exception>(()=>new RescueOptionsStore(path).Save(new()));
        Assert.Equal("original",File.ReadAllText(path)); Assert.Single(Directory.GetFiles(dir));
    }
    [Fact]
    public void Save_rejects_invalid_preferences_before_writing()
    {
        var path = Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".json");
        Assert.Throws<ArgumentException>(()=>new RescueOptionsStore(path).Save(new(RefreshSeconds:0)));
        Assert.False(File.Exists(path));
    }
    [Theory]
    [InlineData("not json")]
    [InlineData("{\"Schema\":99,\"RefreshSeconds\":15}")]
    [InlineData("{\"Schema\":1,\"RefreshSeconds\":0}")]
    public void Corrupt_unknown_schema_and_invalid_interval_are_visible(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid()+".json");
        File.WriteAllText(path, json);
        var loaded = new RescueOptionsStore(path).Load();
        Assert.Contains("could not", loaded.Message);
        Assert.False(loaded.Value.LocalDiagnostics);
        Assert.Equal(json, File.ReadAllText(path));
    }

    [Fact]
    public void Valid_saved_options_are_read()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid()+".json");
        File.WriteAllText(path, "{\"Schema\":1,\"RefreshSeconds\":30,\"ReducedMotion\":true,\"LocalDiagnostics\":false}");
        Assert.Equal(30, new RescueOptionsStore(path).Load().Value.RefreshSeconds);
    }

    [Fact]
    public void Missing_file_uses_private_low_resource_defaults()
    {
        var store = new RescueOptionsStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid()+".json"));
        var loaded = store.Load();
        Assert.Equal(15, loaded.Value.RefreshSeconds);
        Assert.True(loaded.Value.ReducedMotion);
        Assert.False(loaded.Value.LocalDiagnostics);
        Assert.Equal("Defaults; no saved app preferences.", loaded.Message);
    }
}
