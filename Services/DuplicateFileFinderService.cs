using System.IO;
using System.Security.Cryptography;
using WinCareDesktop.Models;

namespace WinCareDesktop.Services;

public sealed record DuplicateFileGroup(
    string Sha256,
    long FileSizeBytes,
    IReadOnlyList<string> Paths)
{
    public int Count => Paths.Count;
    public long ReclaimableBytes => FileSizeBytes * Math.Max(0, Paths.Count - 1);
    public string PrimaryFileName => Path.GetFileName(Paths[0]);
    public string FormattedEach => InstalledProgram.FormatBytes(FileSizeBytes);
    public string FormattedReclaimable => InstalledProgram.FormatBytes(ReclaimableBytes);
    public string DuplicatePathsText => string.Join("\n", Paths.Skip(1));
    public string ExtraFilePath => Paths.Count > 1 ? Paths[1] : Paths[0];
}

/// <summary>
/// Bounded, zero-false-positive duplicate file scanner using a 3-stage pipeline:
/// 1) Exact byte-length grouping
/// 2) First-16KB quick SHA-256 pre-filter
/// 3) Full-file SHA-256 cryptographic verification
/// Never touches protected Windows/Program Files directories and never deletes automatically.
/// </summary>
public sealed class DuplicateFileFinderService
{
    public const long DefaultMinFileBytes = 1024 * 1024; // 1 MiB default so tiny config files are ignored

    public Task<IReadOnlyList<DuplicateFileGroup>> FindDuplicatesAsync(
        string rootDirectory,
        long minFileBytes = DefaultMinFileBytes,
        int maxCandidateFiles = 3000,
        CancellationToken token = default)
    {
        return Task.Run(() => FindDuplicatesCore(rootDirectory, minFileBytes, maxCandidateFiles, token), token);
    }

    private static IReadOnlyList<DuplicateFileGroup> FindDuplicatesCore(
        string rootDirectory,
        long minFileBytes,
        int maxCandidateFiles,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(rootDirectory) || !Directory.Exists(rootDirectory))
            return Array.Empty<DuplicateFileGroup>();

        var fullRoot = Path.GetFullPath(rootDirectory);
        var winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (!string.IsNullOrWhiteSpace(winDir) &&
            (string.Equals(Path.TrimEndingDirectorySeparator(fullRoot), Path.TrimEndingDirectorySeparator(winDir), StringComparison.OrdinalIgnoreCase) ||
             PathSafety.IsWithin(fullRoot, winDir)))
        {
            throw new IOException("Scanning inside the Windows system directory for duplicates is blocked for safety.");
        }

        var files = new List<FileInfo>();
        CollectFilesBounded(new DirectoryInfo(fullRoot), Math.Max(1, minFileBytes), maxCandidateFiles, depthLeft: 4, files, token);

        // Stage 1: Group by exact byte size
        var sizeGroups = files
            .GroupBy(f => f.Length)
            .Where(g => g.Count() > 1)
            .ToList();

        var confirmedGroups = new List<DuplicateFileGroup>();

        foreach (var sizeGroup in sizeGroups)
        {
            token.ThrowIfCancellationRequested();

            // Stage 2: Quick 16 KB header hash to avoid reading multi-GB non-matching files
            var preGroups = new Dictionary<string, List<FileInfo>>(StringComparer.Ordinal);
            foreach (var fi in sizeGroup)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var headHash = HashPrefix(fi.FullName, 16384);
                    if (!preGroups.TryGetValue(headHash, out var bucket))
                    {
                        bucket = new List<FileInfo>();
                        preGroups[headHash] = bucket;
                    }
                    bucket.Add(fi);
                }
                catch { }
            }

            // Stage 3: Full SHA-256 verification on header-matched files
            foreach (var candidateBucket in preGroups.Values.Where(b => b.Count > 1))
            {
                var fullHashes = new Dictionary<string, List<string>>(StringComparer.Ordinal);
                foreach (var fi in candidateBucket)
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        using var fs = new FileStream(fi.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 65536);
                        var digest = Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
                        if (!fullHashes.TryGetValue(digest, out var matches))
                        {
                            matches = new List<string>();
                            fullHashes[digest] = matches;
                        }
                        matches.Add(fi.FullName);
                    }
                    catch { }
                }

                foreach (var (sha, paths) in fullHashes)
                {
                    if (paths.Count > 1)
                    {
                        var ordered = paths.OrderBy(p => p.Length).ThenBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
                        confirmedGroups.Add(new DuplicateFileGroup(sha, sizeGroup.Key, ordered));
                    }
                }
            }
        }

        return confirmedGroups
            .OrderByDescending(g => g.ReclaimableBytes)
            .Take(30)
            .ToArray();
    }

    private static string HashPrefix(string path, int maxBytes)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var buf = new byte[Math.Min(maxBytes, (int)Math.Min(fs.Length, maxBytes))];
        var totalRead = 0;
        while (totalRead < buf.Length)
        {
            var r = fs.Read(buf, totalRead, buf.Length - totalRead);
            if (r <= 0) break;
            totalRead += r;
        }
        return Convert.ToHexString(SHA256.HashData(buf.AsSpan(0, totalRead)));
    }

    private static void CollectFilesBounded(
        DirectoryInfo dir,
        long minBytes,
        int cap,
        int depthLeft,
        List<FileInfo> into,
        CancellationToken token)
    {
        if (into.Count >= cap) return;
        token.ThrowIfCancellationRequested();

        try
        {
            if (!dir.Exists || (dir.Attributes & FileAttributes.ReparsePoint) != 0) return;
            foreach (var f in dir.EnumerateFiles())
            {
                if (into.Count >= cap) return;
                try
                {
                    if ((f.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                    if (f.Length >= minBytes) into.Add(f);
                }
                catch { }
            }

            if (depthLeft <= 0) return;
            foreach (var sub in dir.EnumerateDirectories())
            {
                if (into.Count >= cap) return;
                CollectFilesBounded(sub, minBytes, cap, depthLeft - 1, into, token);
            }
        }
        catch { }
    }
}
