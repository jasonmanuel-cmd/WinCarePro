using System.IO;
using System.Text.Json;

namespace WinCareDesktop.Services;

public class ScheduleConfig
{
    public bool IsEnabled { get; set; } = false;
    public string Frequency { get; set; } = "Daily"; // Daily, Weekly, OnStartup
    public string TimeOfDay { get; set; } = "03:00";
    public bool CleanTempFiles { get; set; } = true;
    public bool EmptyRecycleBin { get; set; }
    public bool OptimizeRam { get; set; }
    public bool FlushDns { get; set; } = false;
    public DateTime? LastRun { get; set; }
    public int ConsentVersion { get; set; }
    public string LastResult { get; set; } = "Not run";
}

public partial class SchedulerService
{
    private readonly string _configPath;

    public SchedulerService(string? configPath = null)
        {
            _configPath = configPath ?? Path.Combine(SafetyMode.DataRoot, "schedule.json");
        }

    public ScheduleConfig LoadConfig()
    {
        try
        {
            if (File.Exists(_configPath))
            {
                var json = File.ReadAllText(_configPath);
                var config = JsonSerializer.Deserialize<ScheduleConfig>(json) ?? new ScheduleConfig();
                config.IsEnabled = false; // Never revive a schedule without fresh consent in this session.
                return config;
            }
        }
        catch { /* Fallback */ }
        return new ScheduleConfig();
    }

    public void SaveConfig(ScheduleConfig config)
    {
        try
        {
            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            Directory.CreateDirectory(Path.GetDirectoryName(_configPath)!);
            File.WriteAllText(_configPath + ".tmp", json);
            File.Move(_configPath + ".tmp", _configPath, true);
        }
        catch { throw; }
    }
}
