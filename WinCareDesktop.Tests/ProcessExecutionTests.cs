using WinCareDesktop.Services;
using Xunit;

namespace WinCareDesktop.Tests;

/// <summary>
/// RunProcess previously did ReadToEnd() on stdout and stderr *before*
/// WaitForExit(timeoutMs). Any child that fills the ~4KB pipe buffer blocks
/// forever on the read, so the timeout never applied and the UI froze. These
/// tests pin the corrected ordering.
/// </summary>
public class ProcessExecutionTests
{
    private readonly SystemOptimizerService _svc = new();

    /// <summary>
    /// Number of write calls and characters per call.
    /// </summary>
    /// <remarks>
    /// The pipe buffer is 4-64KB depending on the pipe, so 300KB is
    /// comfortably past it on any machine. What matters to these tests is only
    /// that the child blocks on a full pipe until the parent drains it, which
    /// 300KB guarantees. More volume buys nothing and costs wall-clock time,
    /// and wall-clock time is what made these tests flaky.
    /// </remarks>
    private const int PayloadWrites = 6000;
    private const int PayloadChars = 50;

    /// <summary>
    /// Budget for a child that takes about 2s to produce its output.
    /// </summary>
    /// <remarks>
    /// These were previously 30s against a child that took 5.6s to start
    /// producing anything. xUnit runs test collections in parallel, so three
    /// of these plus a 40s Appx enumeration were collectively exhausting the
    /// old budget and timing out, which made the suite fail intermittently for
    /// reasons that had nothing to do with the code under test. A tighter,
    /// genuinely fast child means the tests now either pass or reveal a real
    /// regression.
    /// </remarks>
    private const int TimeoutMs = 15_000;

    /// <summary>
    /// Writes <see cref="PayloadWrites"/> chunks straight to the console rather
    /// than building a string first, so the parent has to drain the pipe while
    /// the child is still producing. That is the deadlock these tests exist to
    /// pin.
    /// </summary>
    private static string PayloadCommand(string channel) =>
        $"-NoProfile -Command \"1..{PayloadWrites} | ForEach-Object "
        + $"{{ [Console]::{channel}.Write(('x' * {PayloadChars})) }}\"";

    [Fact]
    public void Large_stdout_does_not_deadlock()
    {
        // Far beyond the pipe buffer, so the old implementation would hang.
        var (code, stdout, _) =
            _svc.RunProcessPublic("powershell", PayloadCommand("Out"), TimeoutMs);

        Assert.Equal(0, code);
        Assert.True(stdout.Length > 100_000,
            $"expected a large payload, got {stdout.Length} chars");
    }

    [Fact]
    public void Large_stderr_does_not_deadlock()
    {
        var (code, _, stderr) =
            _svc.RunProcessPublic("powershell", PayloadCommand("Error"), TimeoutMs);

        Assert.Equal(0, code);
        Assert.True(stderr.Length > 100_000,
            $"expected a large stderr payload, got {stderr.Length} chars");
    }

    [Fact]
    public void Large_output_on_both_pipes_simultaneously_does_not_deadlock()
    {
        // Both pipes filled at once is the case the old ordering really broke on:
        // a child blocked writing to stderr can never reach the parent's stdout
        // read, and neither read completes. Writing both inside one loop body
        // matters - two separate statements would not interleave the writes
        // and would not reproduce the deadlock.
        var both =
            $"-NoProfile -Command \"1..{PayloadWrites} | ForEach-Object "
            + $"{{ [Console]::Out.Write(('o' * {PayloadChars})); "
            + $"[Console]::Error.Write(('e' * {PayloadChars})) }}\"";

        var (code, stdout, stderr) = _svc.RunProcessPublic("powershell", both, TimeoutMs);

        Assert.Equal(0, code);
        Assert.True(stdout.Length > 100_000,
            $"expected a large stdout payload, got {stdout.Length} chars");
        Assert.True(stderr.Length > 100_000,
            $"expected a large stderr payload, got {stderr.Length} chars");
    }

    [Fact]
    public void Timeout_is_actually_enforced()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var (code, _, stderr) = _svc.RunProcessPublic(
            "powershell", "-NoProfile -Command \"Start-Sleep -Seconds 30\"", 3000);
        sw.Stop();

        Assert.NotEqual(0, code);
        Assert.Contains("timed out", stderr, StringComparison.OrdinalIgnoreCase);
        Assert.True(sw.Elapsed.TotalSeconds < 15,
            $"the 3s timeout was not enforced; took {sw.Elapsed.TotalSeconds:F1}s");
    }

    [Fact]
    public void Success_exit_code_and_output_are_returned()
    {
        var (code, stdout, _) = _svc.RunProcessPublic(
            "powershell", "-NoProfile -Command \"Write-Output 'hello-from-child'\"", 15000);

        Assert.Equal(0, code);
        Assert.Contains("hello-from-child", stdout);
    }

    [Fact]
    public void Non_zero_exit_code_is_reported()
    {
        var (code, _, _) = _svc.RunProcessPublic(
            "powershell", "-NoProfile -Command \"exit 3\"", 15000);

        Assert.Equal(3, code);
    }

    [Fact]
    public void Missing_executable_returns_minus_one()
    {
        var (code, _, err) = _svc.RunProcessPublic("definitely-not-a-real-binary.exe", "", 5000);

        Assert.Equal(-1, code);
        Assert.False(string.IsNullOrWhiteSpace(err));
    }
}