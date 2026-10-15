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
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string DisabledRunKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    public List<StartupEntry> GetStartupEntries()
    {
        var entries = new List<StartupEntry>();

        // 1. Current User Run
        ReadRegistryRun(Registry.CurrentUser, RunKey, "User Registry", entries);

        // 2. Local Machine Run
        ReadRegistryRun(Registry.LocalMachine, RunKey, "System Registry", entries);

        // 3. Startup Folder
        ReadStartupFolder(entries);

        return entries;
    }

    private void ReadRegistryRun(RegistryKey root, string subKey, string location, List<StartupEntry> entries)
    {
        try
        {
            using var key = root.OpenSubKey(subKey, false);
            if (key == null) return;

            foreach (var valueName in key.GetValueNames())
            {
                var val = key.GetValue(valueName)?.ToString() ?? string.Empty;
                entries.Add(new StartupEntry
                {
                    Name = valueName,
                    Command = val,
                    Location = location,
                    RegistryKeyPath = subKey,
                    IsEnabled = true,
                    Impact = EstimateImpact(val)
                });
            }
        }
        catch { /* Ignore registry permission errors */ }
    }

    private void ReadStartupFolder(List<StartupEntry> entries)
    {
        try
        {
            var folder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            if (!Directory.Exists(folder)) return;

            foreach (var file in Directory.EnumerateFiles(folder))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                entries.Add(new StartupEntry
                {
                    Name = name,
                    Command = file,
                    Location = "Startup Folder",
                    RegistryKeyPath = folder,
                    IsEnabled = true,
                    Impact = "Medium"
                });
            }
        }
        catch { /* Ignore folder access errors */ }
    }

    public bool ToggleStartupEntry(StartupEntry entry, bool enable)
    {
        try
        {
            if (entry.Location == "User Registry")
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
                if (key != null)
                {
                    if (!enable)
                    {
                        key.DeleteValue(entry.Name, false);
                    }
                    else
                    {
                        key.SetValue(entry.Name, entry.Command);
                    }
                    entry.IsEnabled = enable;
                    return true;
                }
            }
            else if (entry.Location == "Startup Folder")
            {
                var currentPath = entry.Command;
                if (!enable && File.Exists(currentPath))
                {
                    var disabledPath = currentPath + ".disabled";
                    File.Move(currentPath, disabledPath, true);
                    entry.Command = disabledPath;
                    entry.IsEnabled = false;
                    return true;
                }
                else if (enable && currentPath.EndsWith(".disabled") && File.Exists(currentPath))
                {
                    var enabledPath = currentPath[..^9];
                    File.Move(currentPath, enabledPath, true);
                    entry.Command = enabledPath;
                    entry.IsEnabled = true;
                    return true;
                }
            }
        }
        catch { /* Elevation required or file locked */ }
        return false;
    }

    private string EstimateImpact(string command)
    {
        var lower = command.ToLowerInvariant();
        if (lower.Contains("electron") || lower.Contains("discord") || lower.Contains("steam") || lower.Contains("teams") || lower.Contains("spotify"))
            return "High";
        if (lower.Contains("updater") || lower.Contains("helper") || lower.Contains("sync") || lower.Contains("onedrive"))
            return "Medium";
        return "Low";
    }
}
