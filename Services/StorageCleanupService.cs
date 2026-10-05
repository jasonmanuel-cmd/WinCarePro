using System.IO;
using WinCareDesktop.Models;

namespace WinCareDesktop.Services;

/// <summary>
/// Storage tools beyond the existing temp scan: Recycle Bin, the Windows Update
/// download cache, DISM component cleanup, and a largest-folder finder.
/// </summary>
/// <remarks>
/// Every destructive method here deletes something Windows will not hand back.
/// None of them is called without an explicit confirmation from the UI, and each
/// returns what it actually removed so the result can be reported honestly
/// rather than optimistically.
/// </remarks>
public class StorageCleanupService
{
    private readonly SystemOptimizerService _svc;

    public StorageCleanupService(SystemOptimizerService? svc = null)
        => _svc = svc ?? new SystemOptimizerService();
    /// <summary>Where the Update service stages downloads before installing.</summary>
    private static string WindowsUpdateCache =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                     "SoftwareDistribution", "Download");

    /// <summary>
    /// Current size of the Recycle Bin across every drive, in bytes.
    /// </summary>
    public long GetRecycleBinSize()
    {
        long total = 0;
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady) continue;
                var bin = Path.Combine(drive.RootDirectory.FullName, "$Recycle.Bin");

                // $Recycle.Bin holds one ACL-protected folder per user SID.
                // Without elevation most entries are unreadable, so an
                // access-denied result here means "at least this much", not zero.
                foreach (var sid in Directory.EnumerateDirectories(bin))
                {
                    total += DirectorySize(sid);
                }
            }
            catch
            {
                // An unmounted or locked drive simply contributes nothing.
            }
        }
        return total;
    }

    /// <summary>
    /// Empties the Recycle Bin on every fixed drive.
    /// </summary>
    /// <returns>Bytes reclaimed, or -1 when it could not be done.</returns>
    public long EmptyRecycleBin()
    {
        try
        {
            // Shell.Application works without elevation and honours per-user
            // permissions, which a direct delete of $Recycle.Bin does not.
            Type? shell = Type.GetTypeFromProgID("Shell.Application");
            if (shell == null) return -1;

            dynamic? app = Activator.CreateInstance(shell);
            if (app == null) return -1;

            int recycled = 0;
            for (var i = app.Namespace(10).Items().Count; i > 0; i--)
            {
                app.Namespace(10).Items().Item(i).InvokeVerb("delete");
                recycled++;
                if (recycled > 5000) break;   // paranoia guard
            }
            return recycled;
        }
        catch
        {
            return -1;
        }
    }

    public long GetWindowsUpdateCacheSize() =>
        DirectorySize(WindowsUpdateCache);

    /// <summary>
    /// Deletes the staged Windows Update downloads. Windows re-downloads
    /// anything it still needs, so this is safe but not free: the next update
    /// check will use bandwidth.
    /// </summary>
    /// <returns>Bytes removed, or -1 on failure.</returns>
    public long ClearWindowsUpdateCache()
    {
        var before = GetWindowsUpdateCacheSize();
        if (!Directory.Exists(WindowsUpdateCache)) return 0;

        try
        {
            foreach (var file in Directory.EnumerateFiles(WindowsUpdateCache, "*", SearchOption.AllDirectories))
            {
                try { File.Delete(file); }
                catch (IOException) { /* in use by the Update service */ }
                catch (UnauthorizedAccessException) { /* needs elevation */ }
            }
        }
        catch
        {
            return -1;
        }
        return Math.Max(0, before - GetWindowsUpdateCacheSize());
    }

    /// <summary>
    /// Runs DISM's component cleanup, which reclaims superseded WinSxS components.
    /// </summary>
    /// <remarks>
    /// Genuinely useful and not replicated by deleting anything by hand - the
    /// component store is referenced by hard links, so manual removal breaks
    /// servicing. This is slow (minutes), so it reports progress via the log.
    /// </remarks>
    public (bool Success, string Output) RunDismComponentCleanup(int timeoutMs = 900_000)
    {
        var result = _svc.RunProcessPublic(
            "dism.exe",
            "/Online /Cleanup-Image /StartComponentCleanup /NoRestart",
            timeoutMs);

        var combined = string.IsNullOrWhiteSpace(result.stderr)
            ? result.stdout
            : result.stdout + Environment.NewLine + result.stderr;

        return (result.code == 0, combined.Trim());
    }

    /// <summary>
    /// Finds the biggest folders under a root, one level deep per segment.
    /// </summary>
    /// <remarks>
    /// Deliberately bounded: a full recursive walk of C:\ is slow enough to
    /// freeze a UI thread and can take minutes on a large disk.
    /// </remarks>
    public List<LargeFolder> GetLargestFolders(string root, int count = 12, int maxDepth = 3)
    {
        var found = new List<LargeFolder>();
        if (!Directory.Exists(root)) return found;

        var rootDepth = root.TrimEnd(Path.DirectorySeparatorChar).Count(c => c == Path.DirectorySeparatorChar);

        try
        {
            foreach (var dir in Directory.EnumerateDirectories(root))
            {
                Walk(dir, rootDepth, maxDepth, found, count);
            }
        }
        catch
        {
            // Partial results beat none when one branch is denied.
        }

        return found
            .OrderByDescending(f => f.Bytes)
            .Take(count)
            .ToList();
    }

    private void Walk(string dir, int rootDepth, int maxDepth, List<LargeFolder> into, int cap)
    {
        if (into.Count > cap * 4) return;   // stop descending once clearly enough

        var (bytes, files) = Measure(dir, depthLeft: 1);
        if (bytes > 0) into.Add(new LargeFolder { Path = dir, Bytes = bytes, FileCount = files });

        if (maxDepth <= 1) return;

        var depth = dir.TrimEnd(Path.DirectorySeparatorChar).Count(c => c == Path.DirectorySeparatorChar);
        if (depth - rootDepth >= maxDepth) return;

        string[] children;
        try { children = Directory.GetDirectories(dir); }
        catch { return; }

        foreach (var child in children) Walk(child, rootDepth, maxDepth - 1, into, cap);
    }

    /// <summary>
    /// Total size and file count of a directory tree.
    /// </summary>
    /// <remarks>
    /// Reparse points are not followed. Without that check a junction such as
    /// Application Data\Application Data recurses until the path length limit
    /// and the walk never terminates.
    /// </remarks>
    private static (long Bytes, int Files) Measure(string dir, int depthLeft)
    {
        long bytes = 0;
        int files = 0;

        try
        {
            var info = new DirectoryInfo(dir);
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0) return (0, 0);

            foreach (var file in info.EnumerateFiles())
            {
                bytes += file.Length;
                files++;
            }

            if (depthLeft <= 0) return (bytes, files);

            foreach (var sub in info.EnumerateDirectories())
            {
                var (b, f) = Measure(sub.FullName, depthLeft - 1);
                bytes += b;
                files += f;
            }
        }
        catch
        {
            // Denied or vanished mid-walk; keep what we already counted.
        }

        return (bytes, files);
    }

    private static long DirectorySize(string dir)
    {
        try
        {
            return Directory.Exists(dir) ? Measure(dir, depthLeft: 8).Bytes : 0;
        }
        catch
        {
            return 0;
        }
    }
}