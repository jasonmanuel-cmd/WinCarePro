using WinCareDesktop.Models;
using WinCareDesktop.Services;
using Xunit;

namespace WinCareDesktop.Tests;

public class HistoryStoreTests : IDisposable
{
    private readonly string _dir;
    private readonly string _path;

    public HistoryStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "wincare_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "history.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public void Load_returns_empty_when_file_missing()
    {
        var store = new HistoryStore(_path);
        Assert.Empty(store.Load());
    }

    [Fact]
    public void Save_then_load_round_trips()
    {
        var store = new HistoryStore(_path);
        var tx = new OptimizationTransaction
        {
            Title = "Purged Solitaire",
            Description = "Uninstalled Microsoft.MicrosoftSolitaireCollection",
            Kind = UndoKind.ReinstallApp,
            UndoPayload = "Microsoft.MicrosoftSolitaireCollection",
            ReclaimedGb = 0.5
        };

        store.Save(new[] { tx });
        var loaded = store.Load();

        Assert.Single(loaded);
        Assert.Equal("Purged Solitaire", loaded[0].Title);
        Assert.Equal(UndoKind.ReinstallApp, loaded[0].Kind);
        Assert.Equal("Microsoft.MicrosoftSolitaireCollection", loaded[0].UndoPayload);
    }

    [Fact]
    public void Load_survives_corrupt_file()
    {
        File.WriteAllText(_path, "{ this is not json");
        var store = new HistoryStore(_path);
        Assert.Empty(store.Load());
    }

    [Fact]
    public void Load_survives_empty_file()
    {
        File.WriteAllText(_path, "");
        Assert.Empty(new HistoryStore(_path).Load());
    }

    [Fact]
    public void Save_creates_missing_directory()
    {
        var nested = Path.Combine(_dir, "a", "b", "history.json");
        new HistoryStore(nested).Save(new[] { new OptimizationTransaction { Title = "x" } });
        Assert.True(File.Exists(nested));
    }

    [Fact]
    public void Save_caps_entries_at_fifty()
    {
        var many = Enumerable.Range(0, 120)
            .Select(i => new OptimizationTransaction { Title = "op" + i })
            .ToList();
        new HistoryStore(_path).Save(many);
        Assert.Equal(50, new HistoryStore(_path).Load().Count);
    }

    [Fact]
    public void Save_leaves_no_temp_file_behind()
    {
        new HistoryStore(_path).Save(new[] { new OptimizationTransaction { Title = "x" } });
        Assert.False(File.Exists(_path + ".tmp"));
    }
}