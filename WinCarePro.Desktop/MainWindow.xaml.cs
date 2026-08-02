using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace WinCarePro.Desktop;

public partial class MainWindow : Window
{
    private const int BridgeSchemaVersion = 1;
    private static readonly TimeSpan BridgeTimeout = TimeSpan.FromSeconds(30);
    private static readonly HashSet<string> BridgeCommands =
        new(StringComparer.Ordinal) { "dashboard", "scan", "profiles", "timeline", "weekly-report" };

    private CancellationTokenSource? _operationCancellation;
    private bool _operationRunning;

    public ICommand DashboardCommand { get; }
    public ICommand ScanCommand { get; }

    public MainWindow()
    {
        DashboardCommand = new RelayCommand(() => _ = RefreshAsync());
        ScanCommand = new RelayCommand(() => _ = StartScanAsync());
        DataContext = this;
        InitializeComponent();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e) => await RefreshAsync();

    private void Window_Closed(object? sender, EventArgs e) => _operationCancellation?.Cancel();

    private async void StartScan_Click(object sender, RoutedEventArgs e) => await StartScanAsync();

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private void OpenSafetyCenter_Click(object sender, RoutedEventArgs e) =>
        LaunchLegacy("Safety Center", "Full WinCare Pro opened. Review and confirm any care action there.");

    private void OpenUndoCenter_Click(object sender, RoutedEventArgs e) =>
        LaunchLegacy("Undo Center", "Full WinCare Pro opened. Choose Settings, then Undo Center.");

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        if (_operationCancellation is null)
        {
            return;
        }

        StatusText.Text = "Stopping at the next safe boundary…";
        _operationCancellation.Cancel();
    }

    private void ProfileSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ProfileDetailText.Text = ProfileComboBox.SelectedItem is CareProfileOption profile
            ? profile.Recommendation
            : "No care profile selected.";
    }

    private Task RefreshAsync() => RunOperationAsync(
        isScan: false,
        async token =>
        {
            StatusText.Text = "Refreshing local Guided Care data…";
            await LoadDashboardDataAsync(token);
            StatusText.Text = "Guided Care is ready. Review the plan before opening the Safety Center.";
        });

    private Task StartScanAsync() => RunOperationAsync(
        isScan: true,
        async token =>
        {
            StatusText.Text = "Running a read-only guided scan…";
            _ = await RunBridgeAsync("scan", token);
            await LoadDashboardDataAsync(token);
            StatusText.Text = "Scan complete. The ranked review plan and local proof history are updated.";
        });

    private async Task RunOperationAsync(bool isScan, Func<CancellationToken, Task> operation)
    {
        if (_operationRunning)
        {
            StatusText.Text = "Guided Care is already working. Choose Stop before starting another operation.";
            return;
        }

        _operationRunning = true;
        _operationCancellation = new CancellationTokenSource();
        SetBusyState(true);
        try
        {
            await operation(_operationCancellation.Token);
        }
        catch (OperationCanceledException) when (_operationCancellation.IsCancellationRequested)
        {
            var recorded = !isScan || await TryRecordScanCancellationAsync();
            StatusText.Text = recorded
                ? "Guided Care stopped. No repair action was started."
                : "Guided Care stopped, but its cancellation could not be added to local history.";
        }
        catch (TimeoutException)
        {
            var recorded = !isScan || await TryRecordScanCancellationAsync();
            StatusText.Text = recorded
                ? "Guided Care timed out after 30 seconds. No repair action was started; retry when ready."
                : "Guided Care timed out after 30 seconds. No repair action was started, but the timeout could not be added to local history.";
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or InvalidOperationException)
        {
            StatusText.Text = $"Guided Care could not load local data: {ex.Message}";
        }
        finally
        {
            _operationCancellation.Dispose();
            _operationCancellation = null;
            _operationRunning = false;
            SetBusyState(false);
        }
    }

    private void SetBusyState(bool busy)
    {
        StartScanButton.IsEnabled = !busy;
        RefreshButton.IsEnabled = !busy;
        StopButton.IsEnabled = busy;
    }

    private async Task LoadDashboardDataAsync(CancellationToken token)
    {
        RenderDashboard(await RunBridgeAsync("dashboard", token));
        RenderProfiles(await RunBridgeAsync("profiles", token));
        RenderTimeline(await RunBridgeAsync("timeline", token));
        RenderWeeklyReport(await RunBridgeAsync("weekly-report", token));
    }

    private void RenderDashboard(JsonElement data)
    {
        var healthScore = data.GetProperty("health_score").GetDouble();
        var findings = data.GetProperty("findings");
        var capturedAt = data.GetProperty("snapshot_captured_at");
        var timelineCount = data.GetProperty("timeline_count").GetInt32();

        HealthScoreText.Text = healthScore.ToString("0", CultureInfo.CurrentCulture);
        GradeText.Text = data.GetProperty("grade").GetString()!;

        var risks = findings.EnumerateArray()
            .Where(item => IsRisk(item.GetProperty("severity").GetString()))
            .ToList();
        UrgentRiskCountText.Text = risks.Count.ToString(CultureInfo.CurrentCulture);
        RecentChangeText.Text = capturedAt.ValueKind == JsonValueKind.Null
            ? $"No scan yet · {timelineCount} history items"
            : $"{capturedAt.GetString()} · {timelineCount} history items";

        if (capturedAt.ValueKind == JsonValueKind.Null)
        {
            CarePlanList.ItemsSource = new[] { "No baseline yet. Start a guided scan to build a care plan." };
            return;
        }

        CarePlanList.ItemsSource = risks.Count == 0
            ? new[] { "No critical or warning findings in the latest baseline." }
            : risks.Select((item, index) =>
                $"{index + 1}. {item.GetProperty("severity").GetString()}: " +
                $"{item.GetProperty("category").GetString()} — {item.GetProperty("title").GetString()}")
                .ToArray();
    }

    private void RenderProfiles(JsonElement data)
    {
        var selectedId = (ProfileComboBox.SelectedItem as CareProfileOption)?.Id;
        var profiles = data.GetProperty("profiles").EnumerateArray()
            .Select(item => new CareProfileOption(
                item.GetProperty("profile_id").GetString()!,
                item.GetProperty("title").GetString()!,
                string.Join(" ", item.GetProperty("recommendations").EnumerateArray().Select(value => value.GetString()))))
            .ToList();

        ProfileComboBox.ItemsSource = profiles;
        ProfileComboBox.SelectedItem = profiles.FirstOrDefault(profile => profile.Id == selectedId) ?? profiles.FirstOrDefault();
        if (profiles.Count == 0)
        {
            ProfileDetailText.Text = "No care profiles are available.";
        }
    }

    private void RenderTimeline(JsonElement data)
    {
        var events = data.GetProperty("events").EnumerateArray().Reverse().Select(item =>
        {
            var detail = item.GetProperty("detail");
            var detailStatus = detail.TryGetProperty("status", out var status) ? $" · {status.GetString()}" : string.Empty;
            return $"{item.GetProperty("at").GetString()} · {item.GetProperty("event").GetString()}{detailStatus}";
        }).ToArray();

        ProofTimelineList.ItemsSource = events.Length == 0
            ? new[] { "No Guided Care activity has been recorded." }
            : events;
    }

    private void RenderWeeklyReport(JsonElement data)
    {
        var start = NullableNumber(data.GetProperty("score_start"));
        var end = NullableNumber(data.GetProperty("score_end"));
        var completed = data.GetProperty("completed_count").GetInt32();
        var change = data.GetProperty("score_change").GetDouble();
        var risks = data.GetProperty("unresolved_risks").EnumerateArray().Select(item => item.GetString()).ToArray();
        var nextSteps = data.GetProperty("next_steps").EnumerateArray().Select(item => item.GetString()).ToArray();
        var scoreSummary = start is null || end is null
            ? "No scan scores recorded this week."
            : $"Score {start:0} → {end:0} ({change:+0;-0;0}).";
        var riskSummary = risks.Length == 0 ? "No unresolved risks recorded." : $"Unresolved: {string.Join("; ", risks)}.";

        WeeklyReportText.Text = $"{scoreSummary} Verified work: {completed}. {riskSummary} Next: {string.Join("; ", nextSteps)}";
    }

    private async Task<bool> TryRecordScanCancellationAsync()
    {
        try
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            _ = await RunBridgeAsync("scan", cancellation.Token, cancelledScan: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or InvalidOperationException or TimeoutException or OperationCanceledException)
        {
            return false;
        }
    }

    private static async Task<JsonElement> RunBridgeAsync(
        string command,
        CancellationToken cancellationToken,
        bool cancelledScan = false)
    {
        if (!BridgeCommands.Contains(command) || (cancelledScan && command != "scan"))
        {
            throw new InvalidOperationException("A non-allowlisted Guided Care command was refused.");
        }

        var bridge = ResolveBridge()
            ?? throw new IOException("The fixed local Python bridge was not found. Restore the project environment and try again.");
        var startInfo = new ProcessStartInfo
        {
            FileName = bridge.PythonPath,
            WorkingDirectory = Path.GetDirectoryName(bridge.ScriptPath)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add(bridge.ScriptPath);
        startInfo.ArgumentList.Add(command);
        if (cancelledScan)
        {
            startInfo.ArgumentList.Add("--cancel");
        }

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new IOException("The local Guided Care bridge did not start.");
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(BridgeTimeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            KillBridge(process);
            await ObserveOutputAsync(stdoutTask, stderrTask);
            throw new TimeoutException();
        }
        catch
        {
            KillBridge(process);
            await ObserveOutputAsync(stdoutTask, stderrTask);
            throw;
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"The local bridge failed ({SafeDiagnostic(stderr)}).");
        }
        if (!string.IsNullOrWhiteSpace(stderr))
        {
            throw new InvalidDataException($"The local bridge returned unexpected diagnostics ({SafeDiagnostic(stderr)}).");
        }
        if (stdout.Length > 4 * 1024 * 1024)
        {
            throw new InvalidDataException("The local bridge response was too large.");
        }

        using var document = JsonDocument.Parse(stdout);
        return ValidateEnvelope(document.RootElement, command);
    }

    private static JsonElement ValidateEnvelope(JsonElement root, string expectedCommand)
    {
        RequireObject(root, "response");
        if (!root.TryGetProperty("schema_version", out var schema) ||
            !schema.TryGetInt32(out var schemaVersion) || schemaVersion != BridgeSchemaVersion)
        {
            throw new InvalidDataException("The local bridge schema version is not supported.");
        }
        if (!root.TryGetProperty("command", out var command) ||
            command.ValueKind != JsonValueKind.String || command.GetString() != expectedCommand)
        {
            throw new InvalidDataException("The local bridge returned the wrong command envelope.");
        }
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("The local bridge response has no data object.");
        }

        ValidateCommandData(expectedCommand, data);
        return data.Clone();
    }

    private static void ValidateCommandData(string command, JsonElement data)
    {
        switch (command)
        {
            case "dashboard":
                RequireNumber(data, "health_score", 0, 100);
                RequireString(data, "grade");
                RequireObjectProperty(data, "metrics");
                ValidateFindings(RequireArray(data, "findings"));
                RequireStringOrNull(data, "snapshot_captured_at");
                RequireInteger(data, "timeline_count", 0);
                break;
            case "scan":
                var status = RequireString(data, "status");
                if (status is not ("completed" or "cancelled"))
                {
                    throw new InvalidDataException("The scan status is not supported.");
                }
                ValidateFindings(RequireArray(data, "findings"));
                RequireObjectProperty(data, "metrics");
                RequireNumber(data, "health_score", 0, 100);
                _ = RequireArray(data, "breakdown");
                break;
            case "profiles":
                foreach (var profile in RequireArray(data, "profiles").EnumerateArray())
                {
                    RequireObject(profile, "profile");
                    RequireString(profile, "profile_id");
                    RequireString(profile, "title");
                    RequireInteger(profile, "version", 1);
                    ValidateStringArray(RequireArray(profile, "recommendations"), "profile recommendations");
                }
                break;
            case "timeline":
                foreach (var item in RequireArray(data, "events").EnumerateArray())
                {
                    RequireObject(item, "timeline event");
                    RequireString(item, "at");
                    RequireString(item, "event");
                    RequireObjectProperty(item, "detail");
                    if (item.GetProperty("detail").TryGetProperty("status", out var detailStatus) &&
                        detailStatus.ValueKind != JsonValueKind.String)
                    {
                        throw new InvalidDataException("A timeline status is invalid.");
                    }
                }
                break;
            case "weekly-report":
                RequireNumberOrNull(data, "score_start");
                RequireNumberOrNull(data, "score_end");
                RequireNumber(data, "score_change");
                RequireInteger(data, "completed_count", 0);
                ValidateStringArray(RequireArray(data, "unresolved_risks"), "unresolved risks");
                ValidateStringArray(RequireArray(data, "next_steps"), "next steps");
                break;
            default:
                throw new InvalidOperationException("A non-allowlisted Guided Care command was refused.");
        }
    }

    private static void ValidateFindings(JsonElement findings)
    {
        foreach (var finding in findings.EnumerateArray())
        {
            RequireObject(finding, "finding");
            RequireString(finding, "severity");
            RequireString(finding, "category");
            RequireString(finding, "title");
        }
    }

    private static void ValidateStringArray(JsonElement values, string label)
    {
        if (values.EnumerateArray().Any(item => item.ValueKind != JsonValueKind.String))
        {
            throw new InvalidDataException($"The {label} list is invalid.");
        }
    }

    private static JsonElement RequireArray(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException($"The local bridge field '{name}' is invalid.");
        }
        return value;
    }

    private static void RequireObjectProperty(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException($"The local bridge field '{name}' is invalid.");
        }
    }

    private static void RequireObject(JsonElement value, string label)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException($"The local bridge {label} is invalid.");
        }
    }

    private static string RequireString(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new InvalidDataException($"The local bridge field '{name}' is invalid.");
        }
        return value.GetString()!;
    }

    private static void RequireStringOrNull(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) ||
            value.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
        {
            throw new InvalidDataException($"The local bridge field '{name}' is invalid.");
        }
    }

    private static void RequireInteger(JsonElement parent, string name, int minimum)
    {
        if (!parent.TryGetProperty(name, out var value) || !value.TryGetInt32(out var parsed) || parsed < minimum)
        {
            throw new InvalidDataException($"The local bridge field '{name}' is invalid.");
        }
    }

    private static void RequireNumber(JsonElement parent, string name, double minimum = double.MinValue, double maximum = double.MaxValue)
    {
        if (!parent.TryGetProperty(name, out var value) || !value.TryGetDouble(out var parsed) ||
            double.IsNaN(parsed) || double.IsInfinity(parsed) || parsed < minimum || parsed > maximum)
        {
            throw new InvalidDataException($"The local bridge field '{name}' is invalid.");
        }
    }

    private static void RequireNumberOrNull(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value))
        {
            throw new InvalidDataException($"The local bridge field '{name}' is missing.");
        }
        if (value.ValueKind != JsonValueKind.Null)
        {
            RequireNumber(parent, name);
        }
    }

    private static double? NullableNumber(JsonElement value) =>
        value.ValueKind == JsonValueKind.Null ? null : value.GetDouble();

    private static bool IsRisk(string? severity) =>
        severity is not null && (severity.Equals("critical", StringComparison.OrdinalIgnoreCase) ||
                                 severity.Equals("warning", StringComparison.OrdinalIgnoreCase));

    private static void KillBridge(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static async Task ObserveOutputAsync(Task<string> stdoutTask, Task<string> stderrTask)
    {
        try
        {
            await Task.WhenAll(stdoutTask, stderrTask);
        }
        catch (IOException)
        {
        }
    }

    private static string SafeDiagnostic(string stderr)
    {
        var diagnostic = stderr.Trim().Replace('\r', ' ').Replace('\n', ' ');
        return string.IsNullOrEmpty(diagnostic) ? "no diagnostic details" : diagnostic[..Math.Min(diagnostic.Length, 160)];
    }

    private static BridgePaths? ResolveBridge()
    {
        var projectRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var sharedRoot = Path.GetFullPath(Path.Combine(projectRoot, "..", ".."));
        var candidates = new[]
        {
            new BridgePaths(Path.Combine(AppContext.BaseDirectory, "python.exe"), Path.Combine(AppContext.BaseDirectory, "guided_care_cli.py")),
            new BridgePaths(Path.Combine(projectRoot, ".venv", "Scripts", "python.exe"), Path.Combine(projectRoot, "guided_care_cli.py")),
            new BridgePaths(Path.Combine(sharedRoot, ".venv", "Scripts", "python.exe"), Path.Combine(projectRoot, "guided_care_cli.py")),
        };
        return candidates.FirstOrDefault(candidate => File.Exists(candidate.PythonPath) && File.Exists(candidate.ScriptPath));
    }

    private static string? ResolveLegacyEnginePath()
    {
        var projectRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var sharedRoot = Path.GetFullPath(Path.Combine(projectRoot, "..", ".."));
        var candidates = new[]
        {
            Path.Combine(projectRoot, "dist", "WinCarePro.exe"),
            Path.Combine(projectRoot, "WinCarePro.exe"),
            Path.Combine(sharedRoot, "dist", "WinCarePro.exe"),
            Path.Combine(sharedRoot, "WinCarePro.exe"),
            Path.Combine(AppContext.BaseDirectory, "WinCarePro.exe"),
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private void LaunchLegacy(string surface, string successMessage)
    {
        var executable = ResolveLegacyEnginePath();
        if (executable is null)
        {
            StatusText.Text = $"{surface} is unavailable because the full WinCare Pro engine was not found.";
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true });
            StatusText.Text = successMessage;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            StatusText.Text = $"{surface} could not open. No care action was started.";
        }
    }

    private sealed record BridgePaths(string PythonPath, string ScriptPath);
    private sealed record CareProfileOption(string Id, string Title, string Recommendation);

    private sealed class RelayCommand(Action action) : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => action();
    }
}
