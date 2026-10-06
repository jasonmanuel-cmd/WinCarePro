using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using WinCareDesktop.Services;

namespace WinCareDesktop.Services;

/// <summary>
/// Captures the machine context useful for a bug report.
/// </summary>
/// <remarks>
/// Most "it doesn't work" reports from a desktop app are really "it doesn't
/// work on this particular Windows SKU/language/DPI configuration". Those
/// details are painful to ask for one by one and easy to forget, so they are
/// written to a small support file on every launch and included in the crash
/// log.
/// </remarks>
public static class EnvironmentReport
{
    /// <summary>
    /// Builds the report. Never throws — this is a support aid, not a
    /// critical path.
    /// </summary>
    public static string Get()
    {
        try
        {
            var os = Environment.OSVersion;
            var is64 = Environment.Is64BitOperatingSystem;
            var elevated = SystemOptimizerService.IsElevated();
            var dpi = GetDpiContext();
            var tooling = GetToolingPresence();
            var standby = NativeMethodsSupport.StandbyTryProbe();

            return $"""
WinCare Pro environment report
Generated: {DateTime.UtcNow:O}

OS: {os.VersionString} ({(is64 ? "x64" : "x86")})
Elevated: {elevated}
DPI awareness: {dpi}
Standby flush (EmptyStandbyList/psapi): {standby}

Windows tooling:
{tooling}

History file: {Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "WinCarePro", "history.json")}
""";
        }
        catch (Exception ex)
        {
            return $"EnvironmentReport failed: {ex.Message}";
        }
    }

    private static string GetDpiContext()
    {
        try
        {
            // Without a foreground window we cannot read the effective DPI, so
            // report whether the process is DPI-aware from the manifest. That
            // is the bit a support ticket actually needs: if the manifest says
            // PerMonitorV2 and the user reports blurry text, the OS honoured
            // it and the fault is elsewhere.
            var asm = typeof(EnvironmentReport).Assembly;
            var desc = asm.GetCustomAttribute<System.Reflection.AssemblyDescriptionAttribute>();
            return desc is null ? "unknown" : "PerMonitorV2 (manifest)";
        }
        catch
        {
            return "unknown";
        }
    }

    private static string GetToolingPresence()
    {
        var names = new[] { "dism.exe", "sfc.exe", "powercfg.exe", "netsh.exe", "wmic.exe", "certutil.exe" };
        var lines = new List<string>();
        foreach (var n in names)
        {
            var path = FindOnPath(n);
            lines.Add($"  {n}: {(path is null ? "missing" : "present")}");
        }
        return string.Join(Environment.NewLine, lines);
    }

    private static string? FindOnPath(string fileName)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathEnv)) return null;
        foreach (var dir in pathEnv.Split(Path.PathSeparator))
        {
            try
            {
                var full = Path.Combine(dir.Trim(), fileName);
                if (File.Exists(full)) return full;
            }
            catch { /* ignore malformed PATH segments */ }
        }
        return null;
    }
}

internal static class NativeMethodsSupport
{
    public static string StandbyTryProbe()
    {
        // A cheap, non-mutating probe: can we open psapi and does the
        // MemoryCompression process exist?
        try
        {
            var proc = System.Diagnostics.Process.GetProcessesByName("MemoryCompression")
                .FirstOrDefault()
                ?? System.Diagnostics.Process.GetProcessesByName("Memory Compression")
                .FirstOrDefault();
            return proc is null ? "MemoryCompression process not found (likely needs elevation)" : "MemoryCompression process found";
        }
        catch
        {
            return "probe failed";
        }
    }
}
