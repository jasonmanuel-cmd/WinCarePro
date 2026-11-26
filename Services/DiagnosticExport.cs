using System.IO;
using System.Text;
using System.Text.Json;

namespace WinCareDesktop.Services;
public static class DiagnosticExport
{
    public const string Disclosure = "Includes only sample/review times, RAM percentage, system-drive usage/free space, two privacy preferences and threshold guidance. No machine or user names, hardware IDs, file paths, installed apps, processes, command lines, credentials, logs or raw errors. No upload. Measurements and times may still be sensitive; review before sharing.";

    // Construct from numeric/nullable fields only, never serialize a service, environment report or UI log.
    public static string Preview(DiagnosticSnapshot snapshot, DateTimeOffset now) => JsonSerializer.Serialize(new
    {
        schema = "wincare-diagnostics-v1",
        sampleUtc = snapshot.CapturedAt?.ToUniversalTime(),
        reviewedUtc = now.ToUniversalTime(),
        freshness = snapshot.CapturedAt == null ? "Not scanned" : now - snapshot.CapturedAt > TimeSpan.FromMinutes(1) || snapshot.CapturedAt > now ? "Stale" : "Recent at preview time",
        ramUsedPercent = DashboardGuidance.ValidPercent(snapshot.RamPercent) ? snapshot.RamPercent : null,
        systemDriveUsedPercent = DashboardGuidance.ValidPercent(snapshot.DiskPercent) ? snapshot.DiskPercent : null,
        systemDriveFreeGb = DashboardGuidance.ValidNonnegative(snapshot.FreeDiskGb) ? snapshot.FreeDiskGb : null,
        advertisingId = State(snapshot.AdvertisingBlocked),
        tailoredExperiences = State(snapshot.TailoredBlocked),
        guidance = DashboardGuidance.Build(snapshot, now).Select(g => new { g.Title, g.Explanation }),
        disclosure = Disclosure
    }, new JsonSerializerOptions { WriteIndented = true });

    private static string State(bool? blocked) => blocked switch { true => "Off", false => "On", null => "Unknown" };

    public static async Task WriteAsync(string path, string preview)
    {
        // Stage beside the chosen destination to avoid truncating an existing report on a failed write.
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, preview, new UTF8Encoding(false));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
