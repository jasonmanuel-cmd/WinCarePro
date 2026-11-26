namespace WinCareDesktop.Models;

public enum UninstallState { VerifiedRemoved, Pending, RebootRequired, Unknown, Failed }

public record UninstallOutcome(UninstallState State, string Message)
{
    public bool Success => State == UninstallState.VerifiedRemoved;

    public static UninstallOutcome FromClassicExit(int exitCode, bool? stillInstalled)
    {
        if (exitCode is 3010 or 1641)
            return new(UninstallState.RebootRequired, "Vendor reported restart required. Retained until inventory is refreshed after restart; do not retry now.");
        if (exitCode != 0)
            return new(UninstallState.Failed, $"Uninstaller exited {exitCode}; removal not confirmed.");
        return stillInstalled switch
        {
            false => new(UninstallState.VerifiedRemoved, "Removal verified in installed-program registry."),
            true => new(UninstallState.Pending, "Uninstaller exited but application is still registered; removal pending or ineffective. Refresh before retrying."),
            null => new(UninstallState.Unknown, "Uninstaller exited but inventory readback failed; removal unknown. Refresh before retrying.")
        };
    }
}
