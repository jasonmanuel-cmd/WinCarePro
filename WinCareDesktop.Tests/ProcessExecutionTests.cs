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

    [Fact]
    public void Large_stdout_does_not_deadlock()
    {
        // Far beyond the pipe buffer, so the old implementation would hang.
        var (_, stdout, _) = _svc.RunProcessPublic(
            "powershell",
            "-NoProfile -Command \"1..20000 | ForEach-Object { 'x' * 40 }\"",
            30000);

        Assert.True(stdout.Length > 100_000,
            $"expected a large payload, got {stdout.Length} chars");
    }

    [Fact]
    public void Large_stderr_does_not_deadlock()
    {
        var (_, _, stderr) = _svc.RunProcessPublic(
            "powershell",
            "-NoProfile -Command \"1..20000 | ForEach-Object { [Console]::Error.WriteLine('e' * 40) }\"",
            30000);

        Assert.True(stderr.Length > 100_000,
            $"expected a large stderr payload, got {stderr.Length} chars");
    }

    [Fact]
    public void Large_output_on_both_pipes_simultaneously_does_not_deadlock()
    {
        var (code, stdout, stderr) = _svc.RunProcessPublic(
            "powershell",
            "-NoProfile -Command \"1..20000 | ForEach-Object { 'o' * 20; [Console]::Error.WriteLine('e' * 20) }\"",
            30000);

        Assert.Equal(0, code);
        Assert.True(stdout.Length > 50_000);
        Assert.True(stderr.Length > 50_000);
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