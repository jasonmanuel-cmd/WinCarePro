using System.IO;
using WinCareDesktop.Models;

namespace WinCareDesktop.Services;

public sealed record SafeCacheCategorySummary(
    string Id,
    string Name,
    string Description,
    long Bytes,
    int FileCount,
    IReadOnlyList<CleanupCandidate> Files)
{
    public string FormattedSize => InstalledProgram.FormatBytes(Bytes);
}

/// <summary>
/// BleachBit / Wise-inspired safe cache scanner that strictly targets
/// regenerable cache directories ONLY (never cookies, passwords, history,
/// logins, or active user documents).
/// </summary>
public sealed class DeepCacheScannerService
{
    public Func<bool> IsSafeMode { get; set; } = () => SafetyMode.Enabled;
    public Func<IReadOnlyList<(string Id, string Name, string Description, string[] Roots, string Pattern)>>? CategoryProvider { get; set; }

    public IReadOnlyList<SafeCacheCategorySummary> ScanSafeCaches(CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var specs = CategoryProvider?.Invoke() ?? DefaultCategories();
        var summaries = new List<SafeCacheCategorySummary>(specs.Count);

        foreach (var spec in specs)
        {
            token.ThrowIfCancellationRequested();
            var captured = new List<CleanupCandidate>();

            foreach (var root in spec.Roots.Where(r => !string.IsNullOrWhiteSpace(r)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                token.ThrowIfCancellationRequested();
                if (!Directory.Exists(root)) continue;
                try
                {
                    PathSafety.RejectReparsePoints(root);
                    EnumerateBounded(root, spec.Pattern, spec.Name, captured, maxFiles: 2500, depthLeft: 4, token);
                }
                catch (OperationCanceledException) { throw; }
                catch { /* Locked or protected branch skipped safely */ }
            }

            var totalBytes = captured.Sum(f => f.Bytes);
            summaries.Add(new SafeCacheCategorySummary(
                spec.Id,
                spec.Name,
                spec.Description,
                totalBytes,
                captured.Count,
                captured));
        }

        return summaries.OrderByDescending(s => s.Bytes).ToArray();
    }

    public (long ReclaimedBytes, int DeletedFiles, int SkippedFiles) CleanVerifiedSnapshot(
        IEnumerable<SafeCacheCategorySummary> categories,
        CancellationToken token = default)
    {
        if (IsSafeMode()) throw new InvalidOperationException("Action blocked by WinCare safety mode.");
        token.ThrowIfCancellationRequested();

        var allowedRoots = (CategoryProvider?.Invoke() ?? DefaultCategories())
            .SelectMany(c => c.Roots)
            .Where(r => !string.IsNullOrWhiteSpace(r) && Directory.Exists(r))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        long reclaimed = 0;
        int deleted = 0;
        int skipped = 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var cat in categories)
        {
            foreach (var candidate in cat.Files)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var full = Path.GetFullPath(candidate.Path);
                    if (!seen.Add(full)) continue;
                    if (!allowedRoots.Any(r => PathSafety.IsWithin(full, r)))
                    {
                        skipped++;
                        continue;
                    }
                    PathSafety.RejectReparsePoints(full);
                    var info = new FileInfo(full);
                    if (!info.Exists) continue;
                    if (candidate.LastWriteUtc == null ||
                        info.Length != candidate.Bytes ||
                        info.LastWriteTimeUtc != candidate.LastWriteUtc ||
                        info.CreationTimeUtc != candidate.CreatedUtc)
                    {
                        skipped++;
                        continue;
                    }
                    File.Delete(full);
                    reclaimed += candidate.Bytes;
                    deleted++;
                }
                catch
                {
                    // Running browsers/Explorer hold locks on active cache files; skipping is expected and safe.
                    skipped++;
                }
            }
        }

        return (reclaimed, deleted, skipped);
    }

    private static void EnumerateBounded(
        string dir,
        string pattern,
        string categoryName,
        List<CleanupCandidate> into,
        int maxFiles,
        int depthLeft,
        CancellationToken token)
    {
        if (into.Count >= maxFiles) return;
        token.ThrowIfCancellationRequested();

        try
        {
            var di = new DirectoryInfo(dir);
            if (!di.Exists || (di.Attributes & FileAttributes.ReparsePoint) != 0) return;

            foreach (var file in di.EnumerateFiles(pattern))
            {
                token.ThrowIfCancellationRequested();
                if (into.Count >= maxFiles) return;
                try
                {
                    if ((file.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                    into.Add(new CleanupCandidate
                    {
                        Path = file.FullName,
                        Bytes = file.Length,
                        LastWriteUtc = file.LastWriteTimeUtc,
                        CreatedUtc = file.CreationTimeUtc,
                        Category = categoryName
                    });
                }
                catch { }
            }

            if (depthLeft <= 0) return;
            foreach (var sub in di.EnumerateDirectories())
            {
                if (into.Count >= maxFiles) return;
                EnumerateBounded(sub.FullName, pattern, categoryName, into, maxFiles, depthLeft - 1, token);
            }
        }
        catch { }
    }

    private static IReadOnlyList<(string Id, string Name, string Description, string[] Roots, string Pattern)> DefaultCategories()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var temp = Path.GetTempPath();

        var browserRoots = new List<string>();
        if (!string.IsNullOrWhiteSpace(local))
        {
            // Strictly HTTP/Code cache subfolders ONLY — never Cookies, Login Data, History, or Sessions.
            foreach (var rel in new[]
            {
                @"Google\Chrome\User Data\Default\Cache\Cache_Data",
                @"Google\Chrome\User Data\Default\Code Cache",
                @"Microsoft\Edge\User Data\Default\Cache\Cache_Data",
                @"Microsoft\Edge\User Data\Default\Code Cache",
                @"BraveSoftware\Brave-Browser\User Data\Default\Cache\Cache_Data",
                @"BraveSoftware\Brave-Browser\User Data\Default\Code Cache",
            })
            {
                browserRoots.Add(Path.Combine(local, rel));
            }

            var ffProfiles = Path.Combine(local, @"Mozilla\Firefox\Profiles");
            if (Directory.Exists(ffProfiles))
            {
                try
                {
                    foreach (var prof in Directory.EnumerateDirectories(ffProfiles))
                        browserRoots.Add(Path.Combine(prof, @"cache2\entries"));
                }
                catch { }
            }
        }

        var shaderRoots = string.IsNullOrWhiteSpace(local)
            ? Array.Empty<string>()
            : new[]
            {
                Path.Combine(local, "D3DSCache"),
                Path.Combine(local, @"NVIDIA\DXCache"),
                Path.Combine(local, @"AMD\DxCache"),
            };

        var crashRoots = string.IsNullOrWhiteSpace(local)
            ? Array.Empty<string>()
            : new[]
            {
                Path.Combine(local, "CrashDumps"),
                Path.Combine(local, @"Microsoft\Windows\WER\ReportArchive"),
                Path.Combine(local, @"Microsoft\Windows\WER\ReportQueue"),
            };

        var thumbRoots = string.IsNullOrWhiteSpace(local)
            ? Array.Empty<string>()
            : new[] { Path.Combine(local, @"Microsoft\Windows\Explorer") };

        return new (string, string, string, string[], string)[]
        {
            ("temp", "User Temporary Files", "Transient installer & app scratch files in %TEMP%", new[] { temp }, "*"),
            ("browser-cache", "Browser HTTP & Code Caches", "Regenerable web caches only (passwords, cookies & logins are never touched)", browserRoots.ToArray(), "*"),
            ("shader-cache", "DirectX & GPU Shader Caches", "Compiled shader caches (regenerated automatically when games launch)", shaderRoots, "*"),
            ("crash-wer", "Crash Dumps & Error Reports", "Old user-mode crash dumps and archived Windows Error Reporting logs", crashRoots, "*"),
            ("thumb-cache", "Explorer Thumbnail Cache", "Cached folder & image thumbnails (unlocked thumbcache_*.db files only)", thumbRoots, "thumbcache_*.db"),
        };
    }
}
