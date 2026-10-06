using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace WinCareDesktop.Services;

/// <summary>
/// Opt-in update check. This is the only code path that opens a network
/// connection, and it only does so when the user explicitly clicks a button.
/// </summary>
/// <remarks>
/// The app's privacy promise is "no outbound connections". That promise is kept
/// by making the update check a manual action, documented as such. A user who
/// never clicks the button never generates a packet. The manifest URL is read
/// from a local file so shipping a hardcoded endpoint is not mistaken for
/// telemetry.
/// </remarks>
public class UpdateChecker
{
    private readonly HttpClient _http;

    public UpdateChecker(HttpClient? http = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
    }

    /// <summary>
    /// Result of a check. <see cref="IsUpdateAvailable"/> is meaningful only
    /// when <see cref="Error"/> is null.
    /// </summary>
    public record CheckResult(bool IsUpdateAvailable, string CurrentVersion, string? LatestVersion, string? Error);

    /// <summary>
    /// Checks a version manifest. Returns an error result rather than throwing
    /// so the UI can show it plainly.
    /// </summary>
    public async Task<CheckResult> CheckAsync(string manifestUrl, string currentVersion)
    {
        try
        {
            var json = await _http.GetStringAsync(manifestUrl);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("version", out var v))
                return new CheckResult(false, currentVersion, null, "Manifest missing 'version'.");

            var latest = v.GetString();
            var isNewer = latest is not null && CompareVersion(latest, currentVersion) > 0;
            return new CheckResult(isNewer, currentVersion, latest, null);
        }
        catch (Exception ex)
        {
            return new CheckResult(false, currentVersion, null, ex.Message);
        }
    }

    private static int CompareVersion(string a, string b)
    {
        if (Version.TryParse(a, out var va) && Version.TryParse(b, out var vb))
            return va.CompareTo(vb);
        return string.CompareOrdinal(a, b);
    }
}
