using System.IO;
using System.Text.Json;
using WinCareDesktop.Services;
using Xunit;

namespace WinCareDesktop.Tests;
public class DiagnosticExportTests
{
    private static DiagnosticSnapshot Fixture => new(new DateTimeOffset(2026, 1, 2, 12, 0, 0, TimeSpan.Zero), 40, 70, 30, null, false);

    [Fact]
    public async Task Locked_destination_failure_preserves_previous_file_and_removes_staging_file()
    {
        var root = Path.Combine(Path.GetTempPath(), "wincare-export-locked-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "fixture.json");
        try
        {
            await File.WriteAllTextAsync(path, "previous fixture report");
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                var failure = await Record.ExceptionAsync(() => DiagnosticExport.WriteAsync(path, "new fixture report"));
                Assert.True(failure is IOException or UnauthorizedAccessException, $"Expected a refused replacement, got {failure?.GetType().Name ?? "success"}");
            }
            Assert.Equal("previous fixture report", await File.ReadAllTextAsync(path));
            Assert.Single(Directory.GetFiles(root));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Saving_blocks_duplicate_writes_and_preview_changes()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int writes = 0;
        var session = new DiagnosticSession(_ => Task.FromResult(Fixture), Fixture)
        { ChooseExportPath = () => "fixture.json", WriteExport = (_, _) => { writes++; return completion.Task; } };
        session.PrepareExport();
        var preview = session.ExportPreview;
        var save = session.SaveExportAsync();
        Assert.True(session.IsExporting);
        session.CloseExport(); session.PrepareExport(); await session.SaveExportAsync();
        Assert.True(session.IsExportPreviewOpen); Assert.Equal(preview, session.ExportPreview); Assert.Equal(1, writes);
        completion.SetResult(); await save.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.False(session.IsExporting);
        session.CloseExport(); Assert.False(session.IsExportPreviewOpen); Assert.Empty(session.ExportPreview);
    }

    [Fact]
    public void Report_is_an_allowlist_not_a_raw_environment_or_log_dump()
    {
        using var json = JsonDocument.Parse(DiagnosticExport.Preview(Fixture, Fixture.CapturedAt!.Value));
        Assert.Equal(new[] { "schema", "sampleUtc", "reviewedUtc", "freshness", "ramUsedPercent", "systemDriveUsedPercent", "systemDriveFreeGb", "advertisingId", "tailoredExperiences", "guidance", "disclosure" }, json.RootElement.EnumerateObject().Select(p => p.Name));
        Assert.Equal("Unknown", json.RootElement.GetProperty("advertisingId").GetString());
        Assert.Equal("On", json.RootElement.GetProperty("tailoredExperiences").GetString());
        Assert.DoesNotContain("C:\\", json.RootElement.ToString());
        Assert.Contains("No machine", json.RootElement.GetProperty("disclosure").GetString());
    }

    [Fact]
    public void Invalid_metrics_export_null_and_stale_is_explicit()
    {
        using var json = JsonDocument.Parse(DiagnosticExport.Preview(Fixture with { RamPercent = double.NaN, FreeDiskGb = -1 }, Fixture.CapturedAt!.Value.AddMinutes(2)));
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("ramUsedPercent").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("systemDriveFreeGb").ValueKind);
        Assert.Equal("Stale", json.RootElement.GetProperty("freshness").GetString());
    }

    [Fact]
    public async Task Save_writes_exact_preview_to_fixture_file_even_after_rescan()
    {
        var root = Path.Combine(Path.GetTempPath(), "wincare-export-fixture-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "fixture.json");
        try
        {
            var session = new DiagnosticSession(_ => Task.FromResult(Fixture with { RamPercent = 99 }), Fixture);
            session.ChooseExportPath = () => path;
            session.PrepareExport();
            var preview = session.ExportPreview;
            await session.RescanAsync();
            Assert.Equal(preview, session.ExportPreview);
            await session.SaveExportAsync();
            Assert.Equal(preview, await File.ReadAllTextAsync(path));
            Assert.Contains("Saved", session.ExportStatus);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Cancelled_dialog_and_absent_preview_never_write()
    {
        int writes = 0;
        var session = new DiagnosticSession(_ => Task.FromResult(Fixture), Fixture)
        { ChooseExportPath = () => null, WriteExport = (_, _) => { writes++; return Task.CompletedTask; } };
        await session.SaveExportAsync();
        session.PrepareExport();
        await session.SaveExportAsync();
        Assert.Equal(0, writes);
        Assert.Contains("cancelled", session.ExportStatus);
    }

    [Fact]
    public async Task Failed_write_is_visible_without_exposing_private_exception_text()
    {
        var session = new DiagnosticSession(_ => Task.FromResult(Fixture), Fixture)
        { ChooseExportPath = () => "fixture.json", WriteExport = (_, _) => throw new UnauthorizedAccessException("C:\\Users\\Secret") };
        session.PrepareExport(); await session.SaveExportAsync();
        Assert.Contains("failed", session.ExportStatus);
        Assert.DoesNotContain("Secret", session.ExportStatus);
        Assert.False(session.IsExporting);
        Assert.True(session.IsExportPreviewOpen);
    }
}
