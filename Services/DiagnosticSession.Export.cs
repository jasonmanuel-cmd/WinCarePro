using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace WinCareDesktop.Services;
public partial class DiagnosticSession
{
    public Func<string?> ChooseExportPath { get; set; } = () => null;
    public Func<string, string, Task> WriteExport { get; set; } = DiagnosticExport.WriteAsync;
    [ObservableProperty] private string _exportPreview = "";
    [ObservableProperty] private string _exportStatus = "";
    [ObservableProperty] private bool _isExporting;
    [ObservableProperty] private bool _isExportPreviewOpen;
    [RelayCommand]
    public void PrepareExport()
    {
        if (IsExporting) return;
        ExportPreview = DiagnosticExport.Preview(Snapshot, DateTimeOffset.UtcNow);
        IsExportPreviewOpen = true;
        ExportStatus = "Preview only — nothing saved. Save writes exactly this preview, even if you rescan. Reopen the preview to update it.";
    }

    [RelayCommand]
    public void CloseExport()
    {
        if (IsExporting) return;
        IsExportPreviewOpen = false;
        ExportPreview = "";
    }

    [RelayCommand]
    public async Task SaveExportAsync()
    {
        if (IsExporting || !IsExportPreviewOpen || string.IsNullOrEmpty(ExportPreview)) return;
        IsExporting = true;
        var preview = ExportPreview;
        try
        {
            var path = ChooseExportPath();
            if (string.IsNullOrWhiteSpace(path)) { ExportStatus = "Save cancelled. Nothing written."; return; }
            ExportStatus = "Saving the reviewed report locally…";
            await WriteExport(path, preview);
            ExportStatus = "Saved the reviewed report to your chosen file. Nothing uploaded.";
        }
        catch (Exception) { ExportStatus = "Save failed. Check destination permissions and available space, then retry. No upload occurred."; }
        finally { IsExporting = false; }
    }
}
