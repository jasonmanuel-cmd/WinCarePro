using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WinCareDesktop.Services;

/// <summary>
/// Brings an already-running WinCare window to the foreground.
/// </summary>
/// <remarks>
/// Shown a modal "already running" dialog instead at first, but that is the
/// wrong shape for this case: the user double-clicked the icon because they
/// wanted to get to their existing window, not because they wanted to be told
/// they already have one. A dialog makes them click a second time to achieve
/// what one click should have done.
///
/// This is why the window is found by process id rather than by title. Matching
/// on the title is fragile: another process could own a window with that name,
/// and the title changes when a panel is open.
/// </remarks>
public static class WindowActivator
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    /// <summary>Restores a minimised window before activating it.</summary>
    private const int SW_RESTORE = 9;

    /// <summary>
    /// Finds another WinCare instance and gives it the foreground.
    /// </summary>
    /// <returns>True when an existing window was activated.</returns>
    public static bool TryActivateExisting()
    {
        try
        {
            var me = Process.GetCurrentProcess().Id;

            var others = Process.GetProcessesByName("WinCare Pro");
            try
            {
                foreach (var proc in others)
                {
                    if (proc.Id == me) continue;

                    var handle = proc.MainWindowHandle;
                    if (handle == IntPtr.Zero) continue;

                    // A minimised window must be restored first, or
                    // SetForegroundWindow leaves it minimised and the user sees
                    // nothing happen at all.
                    if (IsIconic(handle)) ShowWindow(handle, SW_RESTORE);

                    return SetForegroundWindow(handle);
                }
            }
            finally
            {
                foreach (var proc in others) proc.Dispose();
            }

            return false;
        }
        catch
        {
            // Failing to focus is cosmetic. The second instance still exits, so
            // this never affects correctness.
            return false;
        }
    }
}
