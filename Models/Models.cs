using System.Text.Json.Serialization;

namespace WinCareDesktop.Models;

public class SystemStats
{
    public double Score { get; set; }
    public double RamPercent { get; set; }
    public double StoragePercent { get; set; }
    public double BatteryPercent { get; set; }
    public double ReclaimedGb { get; set; }
    public int TrackersBlocked { get; set; }
    public string ActiveDns { get; set; } = "System default";
    public List<OptimizationTransaction> UndoTransactions { get; set; } = new();
}

/// <summary>What a transaction can actually put back, if anything.</summary>
public enum UndoKind
{
    /// <summary>Deleted files cannot be restored. Display-only rollback.</summary>
    DisplayOnly = 0,
    /// <summary>Re-registers a privacy registry value.</summary>
    PrivacyRegistry = 1,
    /// <summary>Reinstalls an app package.</summary>
    ReinstallApp = 2,
}

public class OptimizationTransaction
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public DateTime Time { get; set; } = DateTime.Now;

    /// <summary>
    /// True only when <see cref="Kind"/> describes a genuine reversal.
    /// Temp-file deletion is irreversible, so those transactions must not
    /// advertise themselves as undoable.
    /// </summary>
    public bool Reversable => Kind != UndoKind.DisplayOnly;

    /// <summary>
    /// Persisted by name so the history file stays readable and survives
    /// renumbering of the enum. Was [JsonIgnore], which meant every restored
    /// entry silently came back as DisplayOnly and lost its revert ability.
    /// </summary>
    public UndoKind Kind { get; set; } = UndoKind.DisplayOnly;

    /// <summary>Payload for the reversal: registry path + value, or package name.</summary>
    public string? UndoPayload { get; set; }

    public double? ReclaimedGb { get; set; }
    public double? PreviousScore { get; set; }
    public double? PreviousRam { get; set; }
    public double? PreviousStorage { get; set; }
}

public class BloatwareApp
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Package { get; set; } = "";
    public string Size { get; set; } = "";
    public bool IsBloat { get; set; }
    public string Reason { get; set; } = "";
}

public class PrivacyShield
{
    public string Name { get; set; } = "";
    public bool Enabled { get; set; }
}

public class DnsResolver : System.ComponentModel.INotifyPropertyChanged
{
    private bool _active;
    private string _ping = "";
    public string Name { get; set; } = "";
    public string Ip { get; set; } = "";
    public string Id { get; set; } = "";

    public string Ping
    {
        get => _ping;
        set { if (_ping == value) return; _ping = value; PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Ping))); }
    }

    public bool Active
    {
        get => _active;
        set { if (_active == value) return; _active = value; PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Active))); }
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>A process shown in the "top memory" list.</summary>
public class ProcessInfo
{
    public string Name { get; set; } = "";
    public int Pid { get; set; }
    public double WorkingSetMb { get; set; }
}

/// <summary>Result of a disk SMART query. Unavailable on most VMs and desktops.</summary>
public class DiskHealth
{
    public bool Available { get; set; }
    public string Status { get; set; } = "Unknown";
    public string Model { get; set; } = "";
}

/// <summary>A single reclaimable item discovered during a temp scan.</summary>
public class CleanupCandidate
{
    public string Path { get; set; } = "";
    public long Bytes { get; set; }
    public string Category { get; set; } = "";
    public bool IsDirectory { get; set; }
}

/// <summary>
/// A program read from the Windows uninstall registry.
///
/// Uninstall is always delegated to the vendor's own uninstall string. WinCare
/// never deletes an application's install folder, because doing so leaves
/// registry keys, services and scheduled tasks behind and breaks uninstall and
/// repair - which is strictly worse than leaving the app installed.
/// </summary>
public class InstalledProgram
{
    public string KeyPath { get; set; } = "";
    public string Name { get; set; } = "";
    public string Publisher { get; set; } = "";
    public string Version { get; set; } = "";
    public string InstallDate { get; set; } = "";

    /// <summary>Size in bytes, or 0 when the installer never recorded one.</summary>
    public long SizeBytes { get; set; }

    public bool SizeKnown { get; set; }

    public string UninstallString { get; set; } = "";
    public string QuietUninstallString { get; set; } = "";
    public string InstallLocation { get; set; } = "";

    /// <summary>True for Windows components and updates, which must not be offered.</summary>
    public bool IsSystemComponent { get; set; }

    public string DisplaySize => SizeKnown ? FormatBytes(SizeBytes) : "size unknown";

    public static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "0 B";
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double v = bytes;
        var i = 0;
        while (v >= 1024 && i < units.Length - 1) { v /= 1024; i++; }
        return $"{v:0.#} {units[i]}";
    }
}

/// <summary>A folder that is consuming disproportionate space.</summary>
public class LargeFolder
{
    public string Path { get; set; } = "";
    public long Bytes { get; set; }
    public int FileCount { get; set; }
}

/// <summary>A power plan the user can switch to.</summary>
public class PowerPlan
{
    public string Guid { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Active { get; set; }
}

/// <summary>
/// One actionable row in a card's tool menu: what it does, what it will touch,
/// and what happened when it ran.
/// </summary>
public class ToolResult
{
    public string Message { get; set; } = "";
    public bool Success { get; set; }
}