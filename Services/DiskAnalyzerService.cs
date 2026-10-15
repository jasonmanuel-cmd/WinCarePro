using System.IO;

namespace WinCareDesktop.Services;

public class DiskItem
{
    public string Name { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public long Bytes { get; set; }
    public double Gigabytes => Math.Round((double)Bytes / (1024 * 1024 * 1024), 2);
    public string FormattedSize => Bytes > 1024 * 1024 * 1024
        ? $"{Gigabytes:F2} GB"
        : $"{Math.Round((double)Bytes / (1024 * 1024), 1):F1} MB";
    public bool IsDirectory { get; set; }
    public double PercentageOfParent { get; set; }
    public string Category { get; set; } = "File";
}

public class DiskAnalyzerService
{
    public async Task<List<DiskItem>> ScanDirectoryAsync(string path, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var results = new List<DiskItem>();
            try
            {
                var dirInfo = new DirectoryInfo(path);
                if (!dirInfo.Exists) return results;

                // Inspect top-level folders and files
                var entries = dirInfo.EnumerateFileSystemInfos();
                long totalFolderBytes = 0;

                var rawList = new List<DiskItem>();

                foreach (var entry in entries)
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    try
                    {
                        if (entry is DirectoryInfo subDir)
                        {
                            progress?.Report($"Scanning {subDir.Name}...");
                            long size = CalculateDirectorySize(subDir, cancellationToken, 2); // Depth limit for fast scan
                            totalFolderBytes += size;
                            rawList.Add(new DiskItem
                            {
                                Name = subDir.Name,
                                FullPath = subDir.FullName,
                                Bytes = size,
                                IsDirectory = true,
                                Category = "Folder"
                            });
                        }
                        else if (entry is FileInfo file)
                        {
                            totalFolderBytes += file.Length;
                            rawList.Add(new DiskItem
                            {
                                Name = file.Name,
                                FullPath = file.FullName,
                                Bytes = file.Length,
                                IsDirectory = false,
                                Category = GetCategory(file.Extension)
                            });
                        }
                    }
                    catch { /* Skip unreadable or access-denied entries */ }
                }

                // Compute relative percentages
                if (totalFolderBytes > 0)
                {
                    foreach (var item in rawList)
                    {
                        item.PercentageOfParent = Math.Round(((double)item.Bytes / totalFolderBytes) * 100, 1);
                    }
                }

                results = rawList.OrderByDescending(x => x.Bytes).Take(50).ToList();
            }
            catch { /* Skip parent access errors */ }

            return results;
        }, cancellationToken);
    }

    private long CalculateDirectorySize(DirectoryInfo dir, CancellationToken ct, int maxDepth)
    {
        if (maxDepth <= 0 || ct.IsCancellationRequested) return 0;
        long size = 0;
        try
        {
            foreach (var f in dir.EnumerateFiles())
            {
                if (ct.IsCancellationRequested) break;
                size += f.Length;
            }
            foreach (var d in dir.EnumerateDirectories())
            {
                if (ct.IsCancellationRequested) break;
                // Ignore junction points / symlinks
                if ((d.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                size += CalculateDirectorySize(d, ct, maxDepth - 1);
            }
        }
        catch { /* Ignore access denied */ }
        return size;
    }

    private string GetCategory(string extension) => extension.ToLowerInvariant() switch
    {
        ".mp4" or ".mkv" or ".avi" or ".mov" => "Video",
        ".zip" or ".rar" or ".7z" or ".tar" or ".gz" => "Archive",
        ".iso" or ".vhd" or ".vhdx" => "Disk Image",
        ".exe" or ".msi" => "Installer",
        ".dll" or ".sys" or ".bin" => "System/Binary",
        ".jpg" or ".jpeg" or ".png" or ".webp" or ".gif" => "Image",
        ".mp3" or ".wav" or ".flac" or ".aac" => "Audio",
        ".pdf" or ".docx" or ".xlsx" => "Document",
        _ => "File"
    };
}
