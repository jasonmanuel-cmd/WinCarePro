namespace WinCareDesktop.Services;

public sealed record RescueOptions(
    int Schema = 1,
    int RefreshSeconds = 15,
    bool ReducedMotion = true,
    bool LocalDiagnostics = false,
    bool DarkDeck = false);

public sealed record OptionsLoad(RescueOptions Value, string Message);

public sealed class RescueOptionsStore(string path)
{
    public void Save(RescueOptions value)
    {
        if (value.Schema != 1 || value.RefreshSeconds is not (5 or 15 or 30 or 60)) throw new ArgumentException("Invalid app preferences.");
        var full = System.IO.Path.GetFullPath(path);
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        var staging = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            System.IO.File.WriteAllText(staging, System.Text.Json.JsonSerializer.Serialize(value));
            System.IO.File.Move(staging, full, true);
        }
        finally { if (System.IO.File.Exists(staging)) System.IO.File.Delete(staging); }
    }

    public OptionsLoad Load()
    {
        try
        {
            var json = System.IO.File.ReadAllText(path);
            var value = System.Text.Json.JsonSerializer.Deserialize<RescueOptions>(json);
            if (value is null || value.Schema != 1 || value.RefreshSeconds is not (5 or 15 or 30 or 60)) throw new FormatException();
            return new(value, "Saved app preferences loaded.");
        }
        catch (System.IO.FileNotFoundException) { return new(new(), "Defaults; no saved app preferences."); }
        catch (System.IO.DirectoryNotFoundException) { return new(new(), "Defaults; no saved app preferences."); }
        catch (Exception) { return new(new(), "App preferences could not be read or use an unsupported schema. Safe defaults applied; saved file retained."); }
    }
}
