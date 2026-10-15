using System.Net.NetworkInformation;

namespace WinCareDesktop.Services;

public class NetworkInterfaceInfo
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public long BytesReceived { get; set; }
    public long BytesSent { get; set; }
    public string FormattedReceived => FormatBytes(BytesReceived);
    public string FormattedSent => FormatBytes(BytesSent);

    private static string FormatBytes(long bytes)
    {
        if (bytes > 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
        if (bytes > 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F1} MB";
        return $"{bytes / 1024.0:F0} KB";
    }
}

public class NetworkMonitorService
{
    public List<NetworkInterfaceInfo> GetActiveInterfaces()
    {
        var list = new List<NetworkInterfaceInfo>();
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                var stats = ni.GetIPStatistics();
                list.Add(new NetworkInterfaceInfo
                {
                    Name = ni.Name,
                    Description = ni.Description,
                    Status = ni.OperationalStatus.ToString(),
                    BytesReceived = stats.BytesReceived,
                    BytesSent = stats.BytesSent
                });
            }
        }
        catch { /* Fallback */ }
        return list;
    }

    public async Task<long> PingHostAsync(string host)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(host, 1500);
            if (reply.Status == IPStatus.Success) return reply.RoundtripTime;
        }
        catch { }
        return -1;
    }
}
