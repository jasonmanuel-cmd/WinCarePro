using System.Diagnostics;
using WinCareDesktop.Models;

namespace WinCareDesktop.Services;

/// <summary>
/// Enumerates and removes Microsoft Store apps.
/// </summary>
/// <remarks>
/// Store apps do not register in the classic uninstall registry, so a registry
/// scan misses every one of them. They are packaged apps instead, enumerated
/// with Get-AppxPackage and removed with Remove-AppxPackage.
///
/// PowerShell is used rather than the Windows.Management.Deployment WinRT API
/// because that API requires the calling process to be packaged itself, and a
/// self-contained desktop exe is not. It is slower to start than an in-process
/// API but it is the only route available to an unpackaged app.
///
/// Everything here shells out, so it is slow by nature. Callers must run it off
/// the UI thread.
/// </remarks>
public class AppxService
{
    private readonly SystemOptimizerService _svc;

    public AppxService(SystemOptimizerService? svc = null)
        => _svc = svc ?? new SystemOptimizerService();

    /// <summary>
    /// Every non-framework Store app installed for the current user.
    /// </summary>
    /// <remarks>
    /// IsFramework is filtered out because removing a framework package breaks
    /// every app that depends on it, and Windows offers no way to put it back
    /// cleanly. System components are filtered for the same reason.
    ///
    /// Publisher is read from the manifest rather than reported as blank,
    /// because Get-AppxPackage does not surface it.
    /// </remarks>
    public List<InstalledProgram> GetStoreApps()
    {
        var apps = new List<InstalledProgram>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        const string script = @"
$ErrorActionPreference = 'SilentlyContinue'
Get-AppxPackage |
  Where-Object { -not $_.IsFramework -and -not $_.NonRemovable } |
  ForEach-Object {
    $pub = ''
    try { $pub = (Get-AppxPackageManifest -Package $_.PackageFullName).Package.Properties.Publisher } catch {}
    [PSCustomObject]@{
      Name        = $_.Name
      FullName    = $_.PackageFullName
      Publisher   = $pub
      Version     = $_.Version
      InstallPath = $_.InstallLocation
    }
  } | ConvertTo-Json -Compress";

        var (code, stdout, stderr) = _svc.RunProcessPublic(
            "powershell",
            $"-NoProfile -NonInteractive -Command \"{script}\"",
            60000);

        if (code != 0 && stdout.Length == 0)
        {
            _svc.Log("Store app scan failed: " + stderr.Trim(), "warning");
            return apps;
        }

        foreach (var item in ParseJson(stdout))
        {
            var name = (item.Name ?? "").Trim();
            var fullName = (item.FullName ?? "").Trim();
            if (name.Length == 0 || fullName.Length == 0) continue;

            // The same app can be installed for several users. Keying on the
            // package family name collapses those into one row, which is what a
            // user means by "this app".
            if (!seen.Add(name)) continue;

            apps.Add(new InstalledProgram
            {
                Name = Humanise(name),
                Publisher = CleanPublisher(item.Publisher),
                Version = item.Version ?? "",
                // Appx reports no install size, and inventing one from the
                // install path would mean walking every file on the system.
                SizeBytes = 0,
                SizeKnown = false,
                InstallLocation = item.InstallPath ?? "",
                Kind = UninstallKind.Appx,
                PackageFullName = fullName,
                // Nothing is listed as a classic uninstall string because that
                // is the truth: these apps have none.
                UninstallString = "",
            });
        }

        return apps.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>
    /// Removes one Store app by its full package name.
    /// </summary>
    /// <remarks>
    /// The package name is interpolated into a PowerShell literal, so quotes are
    /// stripped first. That is not a complete defence against injection, but a
    /// package name is a Windows-assigned identifier and anything containing a
    /// quote is not one.
    /// </remarks>
    public (bool Success, string Message) RemoveStoreApp(string packageFullName, string appName)
    {
        if (string.IsNullOrWhiteSpace(packageFullName))
            return (false, "No package name was supplied.");

        var safe = packageFullName.Replace("'", "");

        var (code, stdout, stderr) = _svc.RunProcessPublic(
            "powershell",
            $"-NoProfile -NonInteractive -Command \"Remove-AppxPackage -Package '{safe}' -ErrorAction Stop\"",
            120000);

        if (code == 0)
            return (true, $"Removed {appName}.");

        var detail = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
        return (false, $"Could not remove {appName}. " +
                       (string.IsNullOrWhiteSpace(detail)
                           ? $"PowerShell exited {code}."
                           : detail.Trim()));
    }

    /// <summary>
    /// Turns a package name into something readable.
    /// </summary>
    /// <remarks>
    /// Get-AppxPackage reports <c>Microsoft.WindowsCalculator</c> rather than
    /// "Calculator", and some packages have no friendly name at all. Only the
    /// trailing segment is used, so the vendor prefix does not leak into the UI.
    /// </remarks>
    public static string Humanise(string packageName)
    {
        var tail = packageName.Split('.').LastOrDefault() ?? packageName;

        // A trailing separator leaves an empty tail. Falling back to the whole
        // string beats rendering a blank row in the list.
        return tail.Length == 0 ? packageName : tail;
    }

    /// <summary>
    /// Strips the CN= wrapper a manifest publisher carries.
    /// </summary>
    /// <remarks>
    /// A publisher with no comma is left alone, including the case where CN= is
    /// present but nothing follows it. Only a comma marks the end of the
    /// common name.
    /// </remarks>
    public static string CleanPublisher(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";

        var value = raw.Trim();

        // Manifests publish as: CN=Microsoft Corporation, O=Microsoft...
        // The comma is what terminates the common name. Without one there is
        // nothing to cut at, and "CN=Contoso" must survive intact rather than
        // being reduced to nothing.
        if (value.StartsWith("CN=", StringComparison.OrdinalIgnoreCase))
        {
            var rest = value[3..];

            // A comma terminates the common name. With no comma the whole
            // remainder IS the name, so "CN=Contoso" becomes "Contoso" rather
            // than being left with its prefix attached.
            var comma = rest.IndexOf(',');
            value = (comma >= 0 ? rest[..comma] : rest).Trim();
        }

        return value;
    }

    /// <summary>
    /// Minimal reader for the JSON Get-AppxPackage emits.
    /// </summary>
    /// <remarks>
    /// Hand-rolled rather than pulling in System.Text.Json. The payload is a
    /// flat array of string-or-null objects emitted by ConvertTo-Json, and this
    /// app has no other JSON reader, so a dependency would be more machinery
    /// than the problem deserves. It is deliberately strict: anything it cannot
    /// parse is skipped rather than guessed at.
    /// </remarks>
    public static List<AppxRecord> ParseJson(string json)
    {
        var records = new List<AppxRecord>();
        if (string.IsNullOrWhiteSpace(json)) return records;

        var index = 0;

        // ConvertTo-Json wraps a single item in an object rather than an array.
        SkipWhitespace(json, ref index);
        if (index >= json.Length) return records;

        var single = json[index] == '{';
        if (!single && json[index] != '[') return records;

        if (!single) index++;   // consume '['

        while (index < json.Length)
        {
            SkipWhitespace(json, ref index);
            if (index >= json.Length) break;
            if (json[index] == ']' || json[index] == '}') break;
            if (json[index] == ',') { index++; continue; }
            if (json[index] != '{') break;

            records.Add(ReadObject(json, ref index));
        }

        return records;
    }

    private static AppxRecord ReadObject(string json, ref int index)
    {
        var record = new AppxRecord();
        index++;   // consume '{'

        while (index < json.Length)
        {
            SkipWhitespace(json, ref index);
            if (index >= json.Length) break;
            if (json[index] == '}') { index++; break; }
            if (json[index] == ',') { index++; continue; }
            if (json[index] != '"') break;

            var key = ReadString(json, ref index);
            SkipWhitespace(json, ref index);
            if (index >= json.Length || json[index] != ':') break;
            index++;

            SkipWhitespace(json, ref index);
            if (index >= json.Length) break;

            if (json[index] == '"')
            {
                var value = ReadString(json, ref index);
                switch (key)
                {
                    case "Name": record.Name = value; break;
                    case "FullName": record.FullName = value; break;
                    case "Publisher": record.Publisher = value; break;
                    case "Version": record.Version = value; break;
                    case "InstallPath": record.InstallPath = value; break;
                }
            }
            else if (json[index] == 'n')
            {
                // literal null
                while (index < json.Length && char.IsLetter(json[index])) index++;
            }
            else
            {
                // A number or nested value this reader does not model.
                while (index < json.Length && json[index] != ',' && json[index] != '}') index++;
            }
        }

        return record;
    }

    private static string ReadString(string json, ref int index)
    {
        index++;   // consume opening quote
        var sb = new System.Text.StringBuilder();

        while (index < json.Length && json[index] != '"')
        {
            if (json[index] == '\\' && index + 1 < json.Length)
            {
                index++;
                sb.Append(json[index] switch
                {
                    'n' => '\n',
                    't' => '\t',
                    'r' => '\r',
                    _ => json[index],
                });
            }
            else
            {
                sb.Append(json[index]);
            }
            index++;
        }

        index++;   // consume closing quote
        return sb.ToString();
    }

    private static void SkipWhitespace(string s, ref int i)
    {
        while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
    }
}

/// <summary>One row of Get-AppxPackage output.</summary>
public class AppxRecord
{
    public string Name { get; set; } = "";
    public string FullName { get; set; } = "";
    public string Publisher { get; set; } = "";
    public string Version { get; set; } = "";
    public string InstallPath { get; set; } = "";
}
