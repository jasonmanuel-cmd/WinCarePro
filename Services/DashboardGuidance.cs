namespace WinCareDesktop.Services;

public sealed record DiagnosticSnapshot(DateTimeOffset? CapturedAt, double? RamPercent, double? DiskPercent,
    double? FreeDiskGb, bool? AdvertisingBlocked, bool? TailoredBlocked);
public sealed record GuidanceItem(string Id, string Title, string Explanation, string Panel, string Action);

public static class DashboardGuidance
{
    public static IReadOnlyList<GuidanceItem> Build(DiagnosticSnapshot snapshot, DateTimeOffset now)
    {
        if (snapshot.CapturedAt == null)
            return new[] { new GuidanceItem("missing", "No guidance scan yet", "Run a read-only scan to collect RAM, system-drive headroom and two privacy preferences. The live cards below are separate observations.", "Dashboard", "Scan now") };
        if (now - snapshot.CapturedAt > TimeSpan.FromMinutes(1) || snapshot.CapturedAt > now)
            return new[] { new GuidanceItem("stale", "Guidance needs a fresh sample", "This sample is over one minute old or its time is ahead of the clock. Rescan before acting; old readings are not current evidence.", "Dashboard", "Rescan now") };
        var rows = new List<GuidanceItem>();
        var ramKnown = ValidPercent(snapshot.RamPercent);
        var diskKnown = ValidPercent(snapshot.DiskPercent) && ValidNonnegative(snapshot.FreeDiskGb);
        if (diskKnown && (snapshot.DiskPercent >= 90 || snapshot.FreeDiskGb < 10))
            rows.Add(new("disk", $"Review disk headroom — {snapshot.DiskPercent:F0}% used, {snapshot.FreeDiskGb:F1} GB free", "The system drive has at least 90% used or less than 10 GB free. Review large files before deciding what to remove; no deletion is automatic.", "Disk Analyzer", "Review disk usage"));
        if (ramKnown && snapshot.RamPercent >= 85)
            rows.Add(new("memory", $"Review memory demand — {snapshot.RamPercent:F0}% used", "RAM usage is at least 85% in this sample. This is not a diagnosis of slowness. Save work and review apps you no longer need; uninstalling is optional and may not reduce current usage.", "App Remover", "Review installed apps"));
        if (snapshot.AdvertisingBlocked == false || snapshot.TailoredBlocked == false)
            rows.Add(new("privacy", "Review optional privacy preferences", "At least one of the two supported current-user privacy settings is on. Turning it off is a preference, not evidence of malware or a performance fix.", "Privacy Shield", "Review privacy settings"));
        var missing = new List<string>();
        if (!ramKnown) missing.Add("RAM");
        if (!diskKnown) missing.Add("system-drive capacity");
        if (snapshot.AdvertisingBlocked == null || snapshot.TailoredBlocked == null) missing.Add("privacy preferences");
        if (missing.Count > 0)
            rows.Add(new("unknown", "Some evidence is unknown", "Unavailable or invalid: " + string.Join(", ", missing) + ". Unknown does not mean healthy or protected. Retry the read-only scan; unsupported or inaccessible data may remain unknown.", "Dashboard", "Retry scan"));
        if (rows.Count == 0)
            rows.Add(new("clear", "No threshold-based next step", "This sample shows RAM below 85%, disk use below 90% with at least 10 GB free, and both supported privacy preferences off. This is not a security check or proof of overall system health.", "Dashboard", "Rescan"));
        return rows;
    }

    public static bool ValidPercent(double? value) => ValidNonnegative(value) && value <= 100;
    public static bool ValidNonnegative(double? value) => value.HasValue && double.IsFinite(value.Value) && value >= 0;
}
