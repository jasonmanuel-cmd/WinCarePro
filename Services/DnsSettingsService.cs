using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Win32;
using WinCareDesktop.Models;
namespace WinCareDesktop.Services;

public record DnsState(string AdapterId, string Name, int Index, bool Automatic, string[] Servers);
public record DnsRecovery(DnsState Before, DnsState Applied);
public class DnsSettingsService
{
    private readonly SystemOptimizerService _history;
    private readonly Func<string, string> _read;
    private readonly Action<string, string> _apply;
    public string LastMessage { get; private set; } = "Select an adapter. IPv4 DNS only; IPv6 unchanged.";
    public DnsSettingsService(SystemOptimizerService history, Func<string, string>? read = null, Action<string, string>? apply = null)
    { _history = history; _read = read ?? ReadNative; _apply = apply ?? ApplyNative; }
    private DnsState Read(string id) => JsonSerializer.Deserialize<DnsState>(_read(id)) ?? throw new IOException("DNS state unavailable.");
    private static bool Same(DnsState a, DnsState b) => a.AdapterId == b.AdapterId && a.Automatic == b.Automatic && (a.Automatic || a.Servers.SequenceEqual(b.Servers));
    public bool Change(string adapterId, string ipv4)
    {
        try
        {
            if (!IPAddress.TryParse(ipv4, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork) throw new ArgumentException("IPv4 DNS address required.");
            var before = Read(adapterId);
            if (before.AdapterId != adapterId) throw new IOException("Adapter identity mismatch.");
            var desired = before with { Automatic = false, Servers = new[] { ip.ToString() } };
            _history.PushUndoTransaction(new OptimizationTransaction { Title = $"IPv4 DNS: {before.Name}", Kind = UndoKind.DnsSettings, UndoPayload = JsonSerializer.Serialize(new DnsRecovery(before, desired)), Description = $"Adapter {before.AdapterId}; prior automatic/static mode and ordered IPv4 servers captured.", Time = DateTime.Now });
            if (!Same(Read(adapterId), before)) throw new IOException("DNS changed while confirming; no write performed.");
            _apply(adapterId, JsonSerializer.Serialize(desired));
            if (!Same(Read(adapterId), desired)) throw new IOException("DNS readback mismatch; recovery retained in history.");
            LastMessage = "IPv4 DNS verified on " + before.Name;
            return true;
        }
        catch (Exception ex) { LastMessage = ex.Message; return false; }
    }
    public bool Restore(string payload)
    {
        try
        {
            var recovery = JsonSerializer.Deserialize<DnsRecovery>(payload) ?? throw new IOException("Invalid DNS recovery record.");
            if (!Same(Read(recovery.Before.AdapterId), recovery.Applied)) throw new IOException("DNS changed externally; restore refused.");
            _apply(recovery.Before.AdapterId, JsonSerializer.Serialize(recovery.Before));
            if (!Same(Read(recovery.Before.AdapterId), recovery.Before)) throw new IOException("DNS restore readback mismatch.");
            LastMessage = "Previous IPv4 DNS mode/server order restored and verified.";
            return true;
        }
        catch (Exception ex) { LastMessage = ex.Message; return false; }
    }
    private static string ReadNative(string id)
    {
        if (!Guid.TryParse(id, out _)) throw new ArgumentException("Invalid adapter GUID.");
        var adapter = NetworkInterface.GetAllNetworkInterfaces().SingleOrDefault(n => n.Id == id && n.OperationalStatus == OperationalStatus.Up) ?? throw new IOException("Selected adapter disconnected or disappeared.");
        var properties = adapter.GetIPProperties();
        using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\" + id) ?? throw new IOException("Adapter DNS configuration unavailable.");
        var configured = key.GetValue("NameServer") as string ?? "";
        var automatic = string.IsNullOrWhiteSpace(configured);
        var servers = automatic ? properties.DnsAddresses.Where(a => a.AddressFamily == AddressFamily.InterNetwork).Select(a => a.ToString()).ToArray() : configured.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries);
        if (servers.Any(s => !IPAddress.TryParse(s, out var address) || address.AddressFamily != AddressFamily.InterNetwork)) throw new IOException("Unsupported DNS configuration; no changes allowed.");
        return JsonSerializer.Serialize(new DnsState(id, adapter.Name, properties.GetIPv4Properties().Index, automatic, servers));
    }
    private void ApplyNative(string id, string json)
    {
        SafetyMode.RequireMutationAllowed();
        var desired = JsonSerializer.Deserialize<DnsState>(json) ?? throw new IOException("Invalid DNS state.");
        var current = JsonSerializer.Deserialize<DnsState>(ReadNative(id))!;
        if (desired.AdapterId != id) throw new IOException("Adapter identity mismatch.");
        void Run(string args)
        {
            var result = _history.RunProcessPublic("netsh", args);
            if (result.code != 0) throw new IOException($"DNS command failed ({result.code}): {result.stderr}");
        }
        if (desired.Automatic) Run($"interface ipv4 set dnsservers name={current.Index} source=dhcp");
        else
        {
            if (desired.Servers.Length == 0) throw new IOException("Empty static DNS state is unsupported.");
            foreach (var s in desired.Servers) if (!IPAddress.TryParse(s, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork) throw new IOException("Invalid IPv4 DNS address.");
            Run($"interface ipv4 set dnsservers name={current.Index} source=static address={desired.Servers[0]} validate=no");
            for (var i = 1; i < desired.Servers.Length; i++) Run($"interface ipv4 add dnsservers name={current.Index} address={desired.Servers[i]} index={i + 1} validate=no");
        }
    }
}
