using System;
using System.IO;
using WinCareDesktop.Services;

namespace WinCareDesktop.Tests;

/// <summary>
/// Builds services for tests.
/// </summary>
/// <remarks>
/// Every service must get its own throwaway history file. Constructing
/// <see cref="SystemOptimizerService"/> with no argument points its
/// <see cref="HistoryStore"/> at the real
/// %LOCALAPPDATA%\WinCarePro\history.json, so any test that pushes an undo
/// transaction writes into the user's actual data - which is what left a
/// "test entry" sitting in the real undo history after a test run.
/// </remarks>
internal static class TestService
{
    public static SystemOptimizerService New() =>
        new(new HistoryStore(NewHistoryPath()));

    public static string NewHistoryPath()
    {
        var dir = Path.Combine(Path.GetTempPath(),
                               "wincare_hist_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "history.json");
    }
}