using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.IO;
using System.Windows;
using WinCareDesktop.Models;
using WinCareDesktop.Services;

namespace WinCareDesktop.ViewModels;

public partial class DefenderViewModel
{
    public SecurityThreatAuditService ThreatAudit { get; set; } = new();
    public Action? NavigateToStartupApps { get; set; }

    public Func<string?> ChooseFileToInspect { get; set; } = () =>
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select a file to inspect SHA-256, signature & Defender status",
            Filter = "Executables & Scripts (*.exe;*.dll;*.msi;*.ps1;*.bat;*.vbs;*.zip)|*.exe;*.dll;*.msi;*.ps1;*.bat;*.vbs;*.zip|All files (*.*)|*.*",
            CheckFileExists = true,
        };
        return dlg.ShowDialog(Application.Current?.MainWindow) == true ? dlg.FileName : null;
    };

    public Func<string, bool> ConfirmTargetedFileScan { get; set; } = path =>
        MessageBox.Show(
            $"Run a targeted Microsoft Defender scan on this file?\n\n{path}\n\n" +
            "If Defender detects malware, it may quarantine or remove the file under your existing Windows Security policy.",
            "Confirm Targeted Defender File Scan",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning) == MessageBoxResult.Yes;

    [ObservableProperty]
    private IReadOnlyList<SecurityAuditFinding> _auditFindings = Array.Empty<SecurityAuditFinding>();

    [ObservableProperty]
    private string _auditSummaryText = "Not scanned yet. 7-vector audit checks Hosts hijacks, WinINET Proxy/PAC interception, Startup LOLBins / %TEMP% persistence, Firewall, UAC, SmartScreen & UEFI Secure Boot.";

    [ObservableProperty]
    private bool _isAuditing;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanScanSelectedFile))]
    private string _inspectedFilePath = "";

    [ObservableProperty]
    private string _fileTriageSummary = "Choose any downloaded executable, archive, or script to verify its SHA-256 hash, Authenticode digital signature, and Mark-of-the-Web origin.";

    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanScanSelectedFile))]
    private bool _isInspectingFile;

    public bool CanScanSelectedFile =>
        !SafetyMode.Enabled && !IsInspectingFile && !string.IsNullOrWhiteSpace(InspectedFilePath) && File.Exists(InspectedFilePath);

    [RelayCommand]
    public void OpenStartupFromAudit() => NavigateToStartupApps?.Invoke();

    [RelayCommand]
    public void CopySecurityAuditReport()
    {
        if (AuditFindings.Count == 0) return;
        var text = AuditSummaryText + Environment.NewLine +
            string.Join(Environment.NewLine, AuditFindings.Select(f => $"[{f.BadgeText}] {f.Category}: {f.Title} — {f.Detail}"));
        try
        {
            Clipboard.SetText(text);
            AuditSummaryText = "Copied security audit report to clipboard.";
        }
        catch { }
    }

    [RelayCommand]
    public async Task RunPersistenceAuditAsync()
    {
        if (IsAuditing) return;
        IsAuditing = true;
        AuditSummaryText = "Inspecting Hosts file, WinINET proxy/PAC settings, and startup persistence vectors…";
        try
        {
            var report = await ThreatAudit.RunAuditAsync();
            AuditFindings = report.Findings;
            AuditSummaryText = $"{report.Summary} (Checked {report.CheckedAt:T} • 100% read-only)";
        }
        catch (Exception)
        {
            AuditSummaryText = "Persistence audit could not complete due to permissions or policy.";
        }
        finally
        {
            IsAuditing = false;
        }
    }

    [RelayCommand]
    public async Task InspectSuspiciousFileAsync()
    {
        if (IsInspectingFile) return;
        var chosen = ChooseFileToInspect();
        if (string.IsNullOrWhiteSpace(chosen)) return;

        IsInspectingFile = true;
        FileTriageSummary = $"Inspecting '{Path.GetFileName(chosen)}'…";
        try
        {
            var report = await ThreatAudit.InspectFileAsync(chosen);
            InspectedFilePath = report.FullPath;
            FileTriageSummary =
                $"File: {report.FullPath} ({InstalledProgram.FormatBytes(report.SizeBytes)})\n" +
                $"SHA-256: {report.Sha256}\n" +
                $"Signature: {report.Assessment}";
        }
        catch (Exception ex)
        {
            InspectedFilePath = "";
            FileTriageSummary = $"File inspection failed: {ex.Message}";
        }
        finally
        {
            IsInspectingFile = false;
        }
    }

    [RelayCommand]
    public async Task ScanSelectedFileWithDefenderAsync()
    {
        if (!CanScanSelectedFile)
        {
            FileTriageSummary = "Targeted file scan unavailable (select an existing file in normal mode).";
            return;
        }
        if (!ConfirmTargetedFileScan(InspectedFilePath))
        {
            FileTriageSummary += "\nTargeted Defender scan cancelled.";
            return;
        }

        IsInspectingFile = true;
        try
        {
            var outcome = await ThreatAudit.ScanFileWithDefenderAsync(InspectedFilePath);
            Audit("Targeted file scan", outcome.StartsWith("ALERT", StringComparison.Ordinal) ? "Threat detected" : "Completed");
            FileTriageSummary += "\n" + outcome;
        }
        catch (Exception ex)
        {
            FileTriageSummary += $"\nTargeted Defender scan failed: {ex.Message}";
        }
        finally
        {
            IsInspectingFile = false;
        }
    }
}
