using System.IO;
using System.Text.Json;
namespace WinCareDesktop.Services;
public sealed record RescueAuditEntry(DateTimeOffset At,string Action,string Outcome);
public sealed class RescueAudit(string? path=null)
{
    private readonly string _path=path ?? Path.Combine(SafetyMode.DataRoot,"rescue-actions.json");
    private static readonly object _gate=new();
    public void Record(string action,string outcome)
    {
        // An explicit path is an isolated file-store capability, not permission for native actions.
        if(path is null)SafetyMode.RequireMutationAllowed();
        lock(_gate)
        {
            var rows=File.Exists(_path)?JsonSerializer.Deserialize<List<RescueAuditEntry>>(File.ReadAllText(_path)) ?? throw new IOException("Invalid audit."):new();
            rows.Add(new(DateTimeOffset.UtcNow,action,outcome));
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp=_path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try {File.WriteAllText(temp,JsonSerializer.Serialize(rows.TakeLast(100)));if(File.Exists(_path))File.Replace(temp,_path,null);else File.Move(temp,_path);}
            finally {if(File.Exists(temp))File.Delete(temp);}
        }
    }
}
