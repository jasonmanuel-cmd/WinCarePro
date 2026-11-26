using System.IO;

namespace WinCareDesktop.Services;

/// <summary>Read-only UI verification mode. Explicit test delegates own their isolated mutations.</summary>
public static class SafetyMode
{
    public static bool Enabled => Environment.GetEnvironmentVariable("WINCARE_SAFE_MODE") == "1" || Environment.GetCommandLineArgs().Contains("--safe-mode");
    public static string DataRoot => Enabled
        ? Environment.GetEnvironmentVariable("WINCARE_DATA_ROOT") ?? Path.Combine(Path.GetTempPath(), "WinCareSafe", Environment.ProcessId.ToString())
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinCarePro");
    public static void RequireMutationAllowed()
    {
        if (Enabled) throw new InvalidOperationException("System changes are disabled in read-only verification mode.");
    }
}
