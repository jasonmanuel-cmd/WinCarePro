namespace WinCareDesktop.Services;

public sealed class DiagnosticReader
{
    private readonly Func<(double usedPercent, long totalBytes, long availableBytes)> _ram;
    private readonly Func<(double usedPercent, double usedGb, double totalGb)> _disk;
    private readonly Func<string, bool?> _privacy;
    public DiagnosticReader(Func<(double, long, long)> ram, Func<(double, double, double)> disk, Func<string, bool?> privacy)
        => (_ram, _disk, _privacy) = (ram, disk, privacy);
    public Task<DiagnosticSnapshot> ReadAsync(CancellationToken token) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        // Timestamp the start, not completion: a slow native read must not make old data look new.
        var at = DateTimeOffset.UtcNow;
        var ram = ReadOrDefault(_ram);
        token.ThrowIfCancellationRequested();
        var disk = ReadOrDefault(_disk);
        token.ThrowIfCancellationRequested();
        var advertising = ReadOrDefault(() => _privacy("Advertising ID"));
        token.ThrowIfCancellationRequested();
        var tailored = ReadOrDefault(() => _privacy("Tailored Experiences"));
        token.ThrowIfCancellationRequested();
        var ramValid = ram.totalBytes > 0 && ram.availableBytes >= 0 && ram.availableBytes <= ram.totalBytes && DashboardGuidance.ValidPercent(ram.usedPercent);
        var diskValid = double.IsFinite(disk.totalGb) && disk.totalGb > 0 && DashboardGuidance.ValidNonnegative(disk.usedGb) && disk.usedGb <= disk.totalGb && DashboardGuidance.ValidPercent(disk.usedPercent);
        return new DiagnosticSnapshot(at, ramValid ? ram.usedPercent : null, diskValid ? disk.usedPercent : null,
            diskValid ? disk.totalGb - disk.usedGb : null, advertising, tailored);
    }, token);

    private static T? ReadOrDefault<T>(Func<T> read)
    {
        try { return read(); }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return default; }
    }
}
