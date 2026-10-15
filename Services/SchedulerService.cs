using System.IO;
using System.Text.Json;

namespace WinCareDesktop.Services;

public class ScheduleConfig
{
    public bool IsEnabled { get; set; } = false;
    public string Frequency { get; set; } = "Daily"; // Daily, Weekly, OnStartup
    public string TimeOfDay { get; set; } = "03:00";
    public bool CleanTempFiles { get; set; } = true;
    public bool EmptyRecycleBin { get; set; } = true;
    public bool OptimizeRam { get; set; } = true;
    public bool FlushDns { get; set; } = false;
    public DateTime? LastRun { get; set; }
}

public class SchedulerService
{
    private readonly string _configPath;

    public SchedulerService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dir = Path.Combine(appData, "WinCarePro");
        Directory.CreateDirectory(dir);
        _configPath = Path.Combine(dir, "schedule.json");
    }

    public ScheduleConfig LoadConfig()
    {
        try
        {
            if (File.Exists(_configPath))
            {
                var json = File.ReadAllText(_configPath);
                return JsonSerializer.Deserialize<ScheduleConfig>(json) ?? new ScheduleConfig();
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
            File.WriteAllText(_configPath, json);
        }
        catch { /* Ignore IO errors */ }
    }
}
