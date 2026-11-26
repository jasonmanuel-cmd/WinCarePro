using System.IO;

namespace WinCareDesktop.Services;

public static class PathSafety
{
    public static bool IsWithin(string path, string root)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(root)) return false;
        var fullPath = Path.GetFullPath(path);
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
    }

    public static void ValidateRemoval(string path)
    {
        if (!Path.IsPathFullyQualified(path) || path.StartsWith(@"\\") || path.IndexOf(':', 2) >= 0)
            throw new IOException("Unsafe or non-local path rejected.");
        path = Path.GetFullPath(path);
        var roots = new[] { Path.GetPathRoot(path)!, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Environment.GetFolderPath(Environment.SpecialFolder.Windows), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) };
        for (var i = 0; i < roots.Length; i++)
        {
            if (string.IsNullOrEmpty(roots[i])) continue;
            if (IsWithin(roots[i], path) || string.Equals(Path.TrimEndingDirectorySeparator(path), Path.TrimEndingDirectorySeparator(roots[i]), StringComparison.OrdinalIgnoreCase) || (i >= 2 && i <= 5 && IsWithin(path, roots[i])))
                throw new IOException("Protected system/profile path rejected.");
        }
        RejectReparsePoints(path);
    }

    public static void RejectReparsePoints(string path)
    {
        for (var current = path; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Reparse point paths are not eligible for removal.");
        if (Directory.Exists(path))
            foreach (var child in Directory.EnumerateFileSystemEntries(path)) RejectReparsePointsBelow(child);
    }

    private static void RejectReparsePointsBelow(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("Reparse point inside target; removal refused.");
        if (Directory.Exists(path)) foreach (var child in Directory.EnumerateFileSystemEntries(path)) RejectReparsePointsBelow(child);
    }
}
