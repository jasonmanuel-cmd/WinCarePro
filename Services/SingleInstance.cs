using System.Threading;

namespace WinCareDesktop.Services;

/// <summary>
/// Ensures only one copy of WinCare Pro runs at a time.
/// </summary>
/// <remarks>
/// This is not just tidiness. Two instances each poll system state every five
/// seconds, each hold its own <see cref="SystemOptimizerService"/>, and each
/// writes to the same undo-history file. Worse, a user running maintenance in
/// one window while the other shows stale readings has no way to tell which
/// numbers are current. A second launch should focus the existing window.
///
/// A named Mutex is used rather than MainWindow because the OS releases a mutex
/// automatically if the process dies, so a crashed or force-killed instance does
/// not leave the app permanently un-launchable. It is also per-session, so two
/// users signed into the same machine each get their own copy rather than the
/// second being silently refused.
/// </remarks>
public static class SingleInstance
{
    private static Mutex? _mutex;

    /// <summary>Identity of the running instance, shared across all users' sessions.</summary>
    private const string MutexName = @"Local\WinCarePro.SingleInstance";

    /// <summary>
    /// Attempts to claim exclusive ownership.
    /// </summary>
    /// <returns>
    /// True when this process is the first instance and should show its window.
    /// False when another instance already holds the mutex, in which case the
    /// caller should exit.
    /// </returns>
    public static bool TryAcquire()
    {
        // AbandonedMutexException is thrown when the previous owner died without
        // releasing. That is exactly the case where we *should* take ownership,
        // so it is caught and treated as success rather than as a failure.
        try
        {
            _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
            return createdNew;
        }
        catch (AbandonedMutexException)
        {
            // The previous instance crashed. We now own the mutex.
            return true;
        }
        catch (Exception)
        {
            // If the mutex cannot be created at all - an exotic sandbox, a
            // policy restriction - refusing to start would be worse than
            // running a second copy. Degrade rather than brick.
            return true;
        }
    }

    /// <summary>Releases ownership. Safe to call when the mutex was never taken.</summary>
    public static void Release()
    {
        try
        {
            _mutex?.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Not the owner, or already released. Nothing to do.
        }
        finally
        {
            _mutex?.Dispose();
            _mutex = null;
        }
    }
}
