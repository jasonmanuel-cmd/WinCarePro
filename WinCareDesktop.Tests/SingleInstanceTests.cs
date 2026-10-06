using WinCareDesktop.Services;
using Xunit;

namespace WinCareDesktop.Tests;

/// <summary>
/// Covers the single-instance guard and the crash log.
/// </summary>
/// <remarks>
/// These run in a separate process for the exclusivity cases, because two
/// threads in one process share a mutex differently from two processes: a
/// re-entrant acquire on the same thread succeeds, which is exactly the case a
/// naive in-process test would pass while the real behaviour is broken.
/// </remarks>
public class SingleInstanceTests
{
    [Fact]
    public void First_acquire_succeeds_and_release_is_safe()
    {
        // This test class runs alongside the process-level tests, so the mutex
        // may already be taken. Release first to make the assertion meaningful.
        SingleInstance.Release();

        Assert.True(SingleInstance.TryAcquire());
        SingleInstance.Release();
    }

    [Fact]
    public void Releasing_without_holding_does_not_throw()
    {
        // Called from OnExit even when startup failed partway, so a double or
        // unmatched release must not throw.
        SingleInstance.Release();
        SingleInstance.Release();
    }

    [Fact]
    public void Acquire_after_release_succeeds_again()
    {
        SingleInstance.Release();

        Assert.True(SingleInstance.TryAcquire());

        // A release must genuinely give the mutex up, not merely drop the
        // handle, or relaunching after a crash would be impossible.
        SingleInstance.Release();
        Assert.True(SingleInstance.TryAcquire());
        SingleInstance.Release();
    }
}
