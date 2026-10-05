using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using WinCareDesktop.Models;

namespace WinCareDesktop.Services;

/// <summary>
/// Persists undo history to %LOCALAPPDATA%\WinCarePro\history.json so it
/// survives an app restart. Writes are atomic (temp file + replace) because a
/// half-written history file would lose every prior action.
/// </summary>
public class HistoryStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _path;
    private readonly object _gate = new();
    private const int MaxEntries = 50;

    public HistoryStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WinCarePro", "history.json");
    }

    public string FilePath => _path;

    public List<OptimizationTransaction> Load()
    {
        lock (_gate)
        {
            try
            {
                if (!File.Exists(_path)) return new List<OptimizationTransaction>();
                var json = File.ReadAllText(_path);
                if (string.IsNullOrWhiteSpace(json)) return new List<OptimizationTransaction>();
                return JsonSerializer.Deserialize<List<OptimizationTransaction>>(json, Options)
                       ?? new List<OptimizationTransaction>();
            }
            catch
            {
                // A corrupt history file must never stop the app from starting.
                return new List<OptimizationTransaction>();
            }
        }
    }

    public void Save(IEnumerable<OptimizationTransaction> transactions)
    {
        lock (_gate)
        {
            try
            {
                var dir = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                var list = transactions.Take(MaxEntries).ToList();
                var json = JsonSerializer.Serialize(list, Options);

                var tmp = _path + ".tmp";
                File.WriteAllText(tmp, json);
                // File.Replace requires the destination to exist; fall back on first write.
                if (File.Exists(_path)) File.Replace(tmp, _path, null);
                else File.Move(tmp, _path);
            }
            catch
            {
                // Persistence is best-effort. Losing history is preferable to crashing.
            }
        }
    }
}