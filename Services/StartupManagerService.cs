using Microsoft.Win32;
using System.IO;

namespace WinCareDesktop.Services;

public class StartupEntry
{
    public string Name { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string RegistryKeyPath { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public string Impact { get; set; } = "Medium";
}

public class StartupManagerService
{
    private readonly string? _startupFolder;
    public StartupManagerService(string? startupFolder = null) => _startupFolder = startupFolder;

    private readonly IDictionary<string, string>? _registry;
    public StartupManagerService(string startupFolder, IDictionary<string, string> registry)
    { _startupFolder = startupFolder; _registry = registry; }

    // Presence is independent of supported REG_SZ enumeration: never overwrite another value kind.
    public Func<string, bool, string, bool>? RunValueExists { get; set; }
    private bool HasRunValue(string location, bool disabled, string name)
    {
        if (RunValueExists != null) return RunValueExists(location, disabled, name);
        if (_registry != null) return _registry.ContainsKey(location + (disabled ? "|Disabled|" : "|Run|") + name);
        var root = location == "User Registry" ? Registry.CurrentUser : Registry.LocalMachine;
        using var key = root.OpenSubKey(disabled ? DisabledRunKey : RunKey);
        return key?.GetValueNames().Contains(name, StringComparer.OrdinalIgnoreCase) == true;
    }

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string DisabledRunKey = @"Software\WinCarePro\DisabledStartup\Run";

    private Dictionary<string, string> ReadRun(string location, bool disabled)
    {
        if (_registry != null)
        {
            var prefix = location + (disabled ? "|Disabled|" : "|Run|");
            return _registry.Where(x => x.Key.StartsWith(prefix, StringComparison.Ordinal)).ToDictionary(x => x.Key[prefix.Length..], x => x.Value);
        }
        var root = location == "User Registry" ? Registry.CurrentUser : Registry.LocalMachine;
        using var key = root.OpenSubKey(disabled ? DisabledRunKey : RunKey);
        var result = new Dictionary<string, string>();
        if (key == null) return result;
        foreach (var name in key.GetValueNames())
        {
            // Only REG_SZ is supported; never destroy an unsupported value kind.
            if (key.GetValueKind(name) == RegistryValueKind.String && key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is string command)
                result[name] = command;
        }
        return result;
    }

    private void WriteRun(string location, bool disabled, string name, string? command)
    {
        if (_registry != null)
        {
            var id = location + (disabled ? "|Disabled|" : "|Run|") + name;
            if (command == null) _registry.Remove(id); else _registry[id] = command;
            return;
        }
        var root = location == "User Registry" ? Registry.CurrentUser : Registry.LocalMachine;
        SafetyMode.RequireMutationAllowed();
        using var key = root.CreateSubKey(disabled ? DisabledRunKey : RunKey);
        if (command == null) key.DeleteValue(name, false);
        else key.SetValue(name, command, RegistryValueKind.String);
        key.Flush();
    }

    public List<StartupEntry> GetStartupEntries()
    {
        var entries = new List<StartupEntry>();
        if (_startupFolder == null || _registry != null)
            foreach (var location in new[] { "User Registry", "System Registry" })
                foreach (var disabled in new[] { false, true })
                    foreach (var pair in ReadRun(location, disabled))
                        entries.Add(new StartupEntry { Name = pair.Key, Command = pair.Value, Location = location, RegistryKeyPath = disabled ? DisabledRunKey : RunKey, IsEnabled = !disabled, Impact = EstimateImpact(pair.Value) });
        ReadStartupFolder(entries);
        return entries;
    }

    private void ReadStartupFolder(List<StartupEntry> entries)
    {
        try
        {
            var folder = _startupFolder ?? Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            if (!Directory.Exists(folder)) return;

            foreach (var file in Directory.EnumerateFiles(folder))
            {
                if (Path.GetFileName(file).Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                var name = Path.GetFileNameWithoutExtension(file);
                entries.Add(new StartupEntry
                {
                    Name = name,
                    Command = file,
                    Location = "Startup Folder",
                    RegistryKeyPath = folder,
                    IsEnabled = !file.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase),
                    Impact = EstimateImpact(file)
                });
            }
        }
        catch { /* Ignore folder access errors */ }
    }

    public bool ToggleStartupEntry(StartupEntry entry, bool enable)
    {
        try
        {
            if (entry.Location is "User Registry" or "System Registry")
            {
                if (_startupFolder != null && _registry == null) return false;
                var sourceDisabled = enable;
                var source = ReadRun(entry.Location, sourceDisabled);
                if (!source.TryGetValue(entry.Name, out var command) || command != entry.Command || HasRunValue(entry.Location, !sourceDisabled, entry.Name)) return false;
                // Persist and verify restoration data BEFORE deleting the original command.
                WriteRun(entry.Location, !sourceDisabled, entry.Name, command);
                if (!ReadRun(entry.Location, !sourceDisabled).TryGetValue(entry.Name, out var saved) || saved != command) return false;
                WriteRun(entry.Location, sourceDisabled, entry.Name, null);
                if (ReadRun(entry.Location, sourceDisabled).ContainsKey(entry.Name)) return false;
                entry.IsEnabled = enable;
                return true;
            }
            else if (entry.Location == "Startup Folder")
            {
                if (_startupFolder == null) SafetyMode.RequireMutationAllowed();
                var currentPath = entry.Command;
                if (!enable && File.Exists(currentPath))
                {
                    var disabledPath = currentPath + ".disabled";
                    File.Move(currentPath, disabledPath, false);
                    entry.Command = disabledPath;
                    entry.IsEnabled = false;
                    return true;
                }
                else if (enable && currentPath.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase) && File.Exists(currentPath))
                {
                    var enabledPath = currentPath[..^9];
                    File.Move(currentPath, enabledPath, false);
                    entry.Command = enabledPath;
                    entry.IsEnabled = true;
                    return true;
                }
            }
        }
        catch { /* Elevation required or file locked */ }
        return false;
    }

    public static string EstimateImpact(string command)
    {
        var lower = command.ToLowerInvariant();
        if (lower.Contains("securityhealthsystray"))
            return "Low";
        if (lower.Contains("electron") || lower.Contains("discord") || lower.Contains("steam") ||
            lower.Contains("teams") || lower.Contains("spotify") || lower.Contains("slack") ||
            lower.Contains("zoom") || lower.Contains("chrome") || lower.Contains("msedge") ||
            lower.Contains("brave") || lower.Contains("firefox") || lower.Contains("epicgames") ||
            lower.Contains("riotclient") || lower.Contains("battle.net") || lower.Contains("overwolf") ||
            lower.Contains("creative cloud") || lower.Contains("adobe") || lower.Contains("docker") ||
            lower.Contains("skype") || lower.Contains("cortana") || lower.Contains("armoury") ||
            lower.Contains("icue") || lower.Contains("razer") || lower.Contains("lghub"))
            return "High";
        if (lower.Contains("updater") || lower.Contains("update") || lower.Contains("helper") ||
            lower.Contains("sync") || lower.Contains("onedrive") || lower.Contains("dropbox") ||
            lower.Contains("googledrive") || lower.Contains("tray") || lower.Contains("agent"))
            return "Medium";
        return "Low";
    }
}
