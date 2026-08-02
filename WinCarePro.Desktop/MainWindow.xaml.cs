using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;

namespace WinCarePro.Desktop;

public partial class MainWindow : Window
{
    private const int BridgeSchemaVersion = 1;
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan TeardownWait = TimeSpan.FromSeconds(1);
    private static readonly HashSet<string> BridgeCommands =
        new(StringComparer.Ordinal) { "dashboard", "scan", "profiles", "timeline", "weekly-report" };
    private static readonly HashSet<string> HealthGrades =
        new(StringComparer.Ordinal) { "Excellent", "Good", "Fair", "Poor", "Critical" };
    private static readonly HashSet<string> FindingSeverities =
        new(StringComparer.OrdinalIgnoreCase) { "Critical", "Warning", "Info", "OK" };
    private static readonly HashSet<string> ProfileIds =
        new(StringComparer.Ordinal) { "gaming", "work", "privacy", "battery", "restore_defaults" };
    private static readonly string[] TimestampFormats =
    {
        "yyyy-MM-dd'T'HH:mm:ss'Z'",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'",
        "yyyy-MM-dd'T'HH:mm:sszzz",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz",
    };

    private CancellationTokenSource? _operationCancellation;
    private bool _operationRunning;
    private bool _scanWasInterrupted;

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

        SetStatus("Stopping at the next safe boundary…");
        _operationCancellation.Cancel();
    }

    private void ProfileSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        AutomationProperties.SetName(
            ProfileComboBox,
            ProfileComboBox.SelectedItem is CareProfileOption selected
                ? $"Care profile: {selected.Title}"
                : "Care profile: none selected");
        SetAccessibleText(
            ProfileDetailText,
            "Selected profile recommendation",
            ProfileComboBox.SelectedItem is CareProfileOption profile
                ? profile.Recommendation
                : "No care profile selected.");
    }

    private Task RefreshAsync() => RunOperationAsync(
        isScan: false,
        async (token, budget) =>
        {
            SetStatus("Refreshing local Guided Care data…");
            await LoadDashboardDataAsync(token, budget);
            SetStatus("Guided Care is ready. Review the plan before opening the Safety Center.");
        });

    private Task StartScanAsync() => RunOperationAsync(
        isScan: true,
        async (token, budget) =>
        {
            SetStatus("Running a read-only guided scan…");
            try
            {
                _ = await RunBridgeAsync("scan", token, budget);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                _scanWasInterrupted = true;
                throw;
            }
            await LoadDashboardDataAsync(token, budget);
            SetStatus("Scan complete. The ranked review plan and local proof history are updated.");
        });

    private async Task RunOperationAsync(bool isScan, Func<CancellationToken, OperationBudget, Task> operation)
    {
        if (_operationRunning)
        {
            SetStatus("Guided Care is already working. Choose Stop before starting another operation.");
            return;
        }

        _operationRunning = true;
        _operationCancellation = new CancellationTokenSource();
        using var budget = new OperationBudget(OperationTimeout);
        using var operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            _operationCancellation.Token,
            budget.DeadlineToken);
        _scanWasInterrupted = false;
        SetBusyState(true);
        try
        {
            await operation(operationCancellation.Token, budget);
        }
        catch (OperationCanceledException) when (_operationCancellation.IsCancellationRequested || budget.DeadlineExpired)
        {
            var interruptedScan = isScan && _scanWasInterrupted;
            var recordResult = interruptedScan
                ? await TryRecordScanCancellationAsync(budget)
                : CancellationRecordResult.NotNeeded;
            var action = _operationCancellation.IsCancellationRequested ? "stopped" : "timed out after 30 seconds";
            var persistence = recordResult switch
            {
                CancellationRecordResult.Recorded => " The interrupted scan was added to local history.",
                CancellationRecordResult.NoTimeRemaining => " The scan stopped locally; its cancellation was not added to history because the 30-second deadline expired.",
                CancellationRecordResult.Failed => " The scan stopped locally, but its cancellation could not be added to history.",
                _ => string.Empty,
            };
            SetStatus($"Guided Care {action}. No repair action was started.{persistence}");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or InvalidOperationException or Win32Exception)
        {
            SetStatus($"Guided Care could not load local data: {ex.Message}");
        }
        finally
        {
            _scanWasInterrupted = false;
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

    private async Task LoadDashboardDataAsync(CancellationToken token, OperationBudget budget)
    {
        RenderDashboard(await RunBridgeAsync("dashboard", token, budget));
        RenderProfiles(await RunBridgeAsync("profiles", token, budget));
        RenderTimeline(await RunBridgeAsync("timeline", token, budget));
        RenderWeeklyReport(await RunBridgeAsync("weekly-report", token, budget));
    }

    private void RenderDashboard(JsonElement data)
    {
        var healthScore = data.GetProperty("health_score").GetDouble();
        var findings = data.GetProperty("findings");
        var capturedAt = data.GetProperty("snapshot_captured_at");
        var timelineCount = data.GetProperty("timeline_count").GetInt32();

        var risks = findings.EnumerateArray()
            .Where(item => IsRisk(item.GetProperty("severity").GetString()))
            .ToList();

        if (capturedAt.ValueKind == JsonValueKind.Null)
        {
            SetAccessibleText(HealthScoreText, "Health score", "Not scanned");
            SetAccessibleText(GradeText, "Health grade", "Not available");
            SetAccessibleText(UrgentRiskCountText, "Risks to review", "Unknown");
            SetAccessibleText(RecentChangeText, "Latest baseline", $"No scan yet · {timelineCount} history items");
            SetAccessibleList(
                CarePlanList,
                "Ranked care plan",
                new[] { "No baseline yet. Start a guided scan to build a care plan." });
            return;
        }

        SetAccessibleText(HealthScoreText, "Health score", healthScore.ToString("0", CultureInfo.CurrentCulture));
        SetAccessibleText(GradeText, "Health grade", data.GetProperty("grade").GetString()!);
        SetAccessibleText(UrgentRiskCountText, "Risks to review", risks.Count.ToString(CultureInfo.CurrentCulture));
        SetAccessibleText(RecentChangeText, "Latest baseline", $"{capturedAt.GetString()} · {timelineCount} history items");
        var plan = risks.Count == 0
            ? new[] { "No critical or warning findings in the latest baseline." }
            : risks.Select((item, index) =>
                $"{index + 1}. {item.GetProperty("severity").GetString()}: " +
                $"{item.GetProperty("category").GetString()} — {item.GetProperty("title").GetString()}")
                .ToArray();
        SetAccessibleList(CarePlanList, "Ranked care plan", plan);
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
        AutomationProperties.SetName(
            ProfileComboBox,
            ProfileComboBox.SelectedItem is CareProfileOption selected
                ? $"Care profile: {selected.Title}"
                : "Care profile: none available");
        if (profiles.Count == 0)
        {
            SetAccessibleText(ProfileDetailText, "Selected profile recommendation", "No care profiles are available.");
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

        SetAccessibleList(ProofTimelineList, "Proof and activity timeline", events.Length == 0
            ? new[] { "No Guided Care activity has been recorded." }
            : events);
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

        SetAccessibleText(
            WeeklyReportText,
            "Weekly care report",
            $"{scoreSummary} Verified work: {completed}. {riskSummary} Next: {string.Join("; ", nextSteps)}");
    }

    private void SetStatus(string message)
    {
        SetAccessibleText(StatusText, "Guided Care status", message);
        try
        {
            var peer = UIElementAutomationPeer.FromElement(StatusText) ??
                       UIElementAutomationPeer.CreatePeerForElement(StatusText) ??
                       new FrameworkElementAutomationPeer(StatusText);
            peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or COMException)
        {
            // UI Automation can be unavailable during startup or teardown; status text still updates visibly.
        }
    }

    private static void SetAccessibleText(TextBlock control, string label, string value)
    {
        control.Text = value;
        AutomationProperties.SetName(control, $"{label}: {value}");
    }

    private static void SetAccessibleList(ListBox control, string label, string[] values)
    {
        control.ItemsSource = values;
        AutomationProperties.SetName(
            control,
            values.Length == 0 ? $"{label}: empty" : $"{label}: {values.Length} items. {values[0]}");
    }

    private async Task<CancellationRecordResult> TryRecordScanCancellationAsync(OperationBudget budget)
    {
        if (budget.DeadlineExpired)
        {
            return CancellationRecordResult.NoTimeRemaining;
        }
        try
        {
            _ = await RunBridgeAsync("scan", budget.DeadlineToken, budget, cancelledScan: true);
            return CancellationRecordResult.Recorded;
        }
        catch (OperationCanceledException) when (budget.DeadlineExpired)
        {
            return CancellationRecordResult.NoTimeRemaining;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or InvalidOperationException or Win32Exception)
        {
            return CancellationRecordResult.Failed;
        }
    }

    private static async Task<JsonElement> RunBridgeAsync(
        string command,
        CancellationToken cancellationToken,
        OperationBudget budget,
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
        try
        {
            if (!process.Start())
            {
                throw new IOException("The local Guided Care bridge did not start.");
            }
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            throw new IOException("The local Guided Care bridge could not start.", ex);
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync(cancellationToken);
            var (stdout, stderr) = await ReadOutputWithinBudgetAsync(
                stdoutTask,
                stderrTask,
                cancellationToken,
                budget);
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
        catch
        {
            KillBridge(process);
            await DrainOutputWithinBudgetAsync(stdoutTask, stderrTask, budget);
            throw;
        }
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
                if (!HealthGrades.Contains(RequireString(data, "grade")))
                {
                    throw new InvalidDataException("The health grade is not supported.");
                }
                RequireObjectProperty(data, "metrics");
                var dashboardFindings = RequireArray(data, "findings");
                ValidateFindings(dashboardFindings);
                RequireTimestampOrNull(data, "snapshot_captured_at");
                RequireInteger(data, "timeline_count", 0);
                if (data.GetProperty("snapshot_captured_at").ValueKind == JsonValueKind.Null &&
                    dashboardFindings.GetArrayLength() != 0)
                {
                    throw new InvalidDataException("An empty dashboard cannot contain findings.");
                }
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
                ValidateStringArray(RequireArray(data, "breakdown"), "scan breakdown");
                break;
            case "profiles":
                foreach (var profile in RequireArray(data, "profiles").EnumerateArray())
                {
                    RequireObject(profile, "profile");
                    if (!ProfileIds.Contains(RequireString(profile, "profile_id")))
                    {
                        throw new InvalidDataException("A care profile identifier is not supported.");
                    }
                    RequireString(profile, "title");
                    RequireInteger(profile, "version", 1);
                    ValidateStringArray(RequireArray(profile, "recommendations"), "profile recommendations");
                }
                break;
            case "timeline":
                foreach (var item in RequireArray(data, "events").EnumerateArray())
                {
                    RequireObject(item, "timeline event");
                    RequireTimestamp(item, "at");
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
                RequireNumberOrNull(data, "score_start", 0, 100);
                RequireNumberOrNull(data, "score_end", 0, 100);
                RequireNumber(data, "score_change", -100, 100);
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
            if (!FindingSeverities.Contains(RequireString(finding, "severity")))
            {
                throw new InvalidDataException("A finding severity is not supported.");
            }
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

    private static void RequireTimestampOrNull(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value))
        {
            throw new InvalidDataException($"The local bridge field '{name}' is invalid.");
        }
        if (value.ValueKind != JsonValueKind.Null)
        {
            RequireTimestamp(parent, name);
        }
    }

    private static void RequireTimestamp(JsonElement parent, string name)
    {
        var value = RequireString(parent, name);
        if (!DateTimeOffset.TryParseExact(
                value,
                TimestampFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out _))
        {
            throw new InvalidDataException($"The local bridge timestamp '{name}' is invalid.");
        }
    }

    private static void RequireInteger(JsonElement parent, string name, int minimum)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number ||
            !value.TryGetInt32(out var parsed) || parsed < minimum)
        {
            throw new InvalidDataException($"The local bridge field '{name}' is invalid.");
        }
    }

    private static void RequireNumber(JsonElement parent, string name, double minimum = double.MinValue, double maximum = double.MaxValue)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number ||
            !value.TryGetDouble(out var parsed) ||
            double.IsNaN(parsed) || double.IsInfinity(parsed) || parsed < minimum || parsed > maximum)
        {
            throw new InvalidDataException($"The local bridge field '{name}' is invalid.");
        }
    }

    private static void RequireNumberOrNull(JsonElement parent, string name, double minimum, double maximum)
    {
        if (!parent.TryGetProperty(name, out var value))
        {
            throw new InvalidDataException($"The local bridge field '{name}' is missing.");
        }
        if (value.ValueKind != JsonValueKind.Null)
        {
            RequireNumber(parent, name, minimum, maximum);
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
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
        }
    }

    private static async Task<(string Stdout, string Stderr)> ReadOutputWithinBudgetAsync(
        Task<string> stdoutTask,
        Task<string> stderrTask,
        CancellationToken cancellationToken,
        OperationBudget budget)
    {
        var outputTask = Task.WhenAll(stdoutTask, stderrTask);
        var remaining = budget.Remaining;
        if (remaining <= TimeSpan.Zero)
        {
            ObserveFault(outputTask);
            throw new OperationCanceledException("The Guided Care operation deadline expired.", budget.DeadlineToken);
        }
        try
        {
            await outputTask.WaitAsync(remaining, cancellationToken);
        }
        catch (TimeoutException ex)
        {
            ObserveFault(outputTask);
            throw new OperationCanceledException("The Guided Care operation deadline expired.", ex, budget.DeadlineToken);
        }
        return (stdoutTask.Result, stderrTask.Result);
    }

    private static async Task DrainOutputWithinBudgetAsync(
        Task<string> stdoutTask,
        Task<string> stderrTask,
        OperationBudget budget)
    {
        var outputTask = Task.WhenAll(stdoutTask, stderrTask);
        var remaining = budget.Remaining;
        var wait = remaining < TeardownWait ? remaining : TeardownWait;
        if (wait <= TimeSpan.Zero)
        {
            ObserveFault(outputTask);
            return;
        }

        try
        {
            await outputTask.WaitAsync(wait, budget.DeadlineToken);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or OperationCanceledException or TimeoutException)
        {
            ObserveFault(outputTask);
        }
    }

    private static void ObserveFault(Task task) =>
        _ = task.ContinueWith(
            completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);

    private static string SafeDiagnostic(string stderr)
    {
        var diagnostic = stderr.Trim().Replace('\r', ' ').Replace('\n', ' ');
        return string.IsNullOrEmpty(diagnostic) ? "no diagnostic details" : diagnostic[..Math.Min(diagnostic.Length, 160)];
    }

    private static BridgePaths? ResolveBridge()
    {
        var installRoot = Path.GetFullPath(AppContext.BaseDirectory);
        var installed = ResolveBridgePair(installRoot, installRoot);
        if (installed is not null)
        {
            return installed;
        }

        var projectRoot = TryGetDevelopmentRoot();
        if (projectRoot is null)
        {
            return null;
        }

        var projectBridge = ResolveBridgePair(projectRoot, projectRoot);
        if (projectBridge is not null)
        {
            return projectBridge;
        }

        var sharedRoot = TryGetWorktreeHostRoot(projectRoot);
        return sharedRoot is null ? null : ResolveBridgePair(sharedRoot, projectRoot);
    }

    private static string? ResolveLegacyEnginePath()
    {
        var installRoot = Path.GetFullPath(AppContext.BaseDirectory);
        var installed = CanonicalFileWithin(Path.Combine(installRoot, "WinCarePro.exe"), installRoot);
        if (installed is not null)
        {
            return installed;
        }

        var projectRoot = TryGetDevelopmentRoot();
        if (projectRoot is null)
        {
            return null;
        }

        var projectCandidates = new[]
        {
            Path.Combine(projectRoot, "dist", "WinCarePro.exe"),
            Path.Combine(projectRoot, "WinCarePro.exe"),
        };
        var projectExecutable = projectCandidates
            .Select(path => CanonicalFileWithin(path, projectRoot))
            .FirstOrDefault(path => path is not null);
        if (projectExecutable is not null)
        {
            return projectExecutable;
        }

        var sharedRoot = TryGetWorktreeHostRoot(projectRoot);
        if (sharedRoot is null)
        {
            return null;
        }
        return new[]
        {
            Path.Combine(sharedRoot, "dist", "WinCarePro.exe"),
            Path.Combine(sharedRoot, "WinCarePro.exe"),
        }.Select(path => CanonicalFileWithin(path, sharedRoot)).FirstOrDefault(path => path is not null);
    }

    private static BridgePaths? ResolveBridgePair(string pythonRoot, string scriptRoot)
    {
        var python = CanonicalFileWithin(Path.Combine(pythonRoot, ".venv", "Scripts", "python.exe"), pythonRoot)
            ?? CanonicalFileWithin(Path.Combine(pythonRoot, "python.exe"), pythonRoot);
        var script = CanonicalFileWithin(Path.Combine(scriptRoot, "guided_care_cli.py"), scriptRoot);
        return python is null || script is null ? null : new BridgePaths(python, script);
    }

    private static string? TryGetDevelopmentRoot()
    {
        var targetFramework = new DirectoryInfo(Path.GetFullPath(AppContext.BaseDirectory));
        var configuration = targetFramework.Parent;
        var bin = configuration?.Parent;
        var project = bin?.Parent;
        var root = project?.Parent;
        return targetFramework.Name.Equals("net8.0-windows", StringComparison.OrdinalIgnoreCase) &&
               configuration is not null &&
               (configuration.Name.Equals("Debug", StringComparison.OrdinalIgnoreCase) ||
                configuration.Name.Equals("Release", StringComparison.OrdinalIgnoreCase)) &&
               bin?.Name.Equals("bin", StringComparison.OrdinalIgnoreCase) == true &&
               project?.Name.Equals("WinCarePro.Desktop", StringComparison.OrdinalIgnoreCase) == true
            ? root?.FullName
            : null;
    }

    private static string? TryGetWorktreeHostRoot(string projectRoot)
    {
        var worktrees = Directory.GetParent(projectRoot);
        return worktrees?.Name.Equals(".worktrees", StringComparison.OrdinalIgnoreCase) == true
            ? worktrees.Parent?.FullName
            : null;
    }

    private static string? CanonicalFileWithin(string candidate, string allowedRoot)
    {
        try
        {
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(allowedRoot));
            var fullPath = Path.GetFullPath(candidate);
            if (!IsWithinRoot(fullPath, root))
            {
                return null;
            }

            if (!File.Exists(fullPath) || ContainsReparsePoint(root, fullPath))
            {
                return null;
            }
            return fullPath;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static bool IsWithinRoot(string path, string root) =>
        path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static bool ContainsReparsePoint(string root, string path)
    {
        var current = root;
        if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
        {
            return true;
        }

        foreach (var component in Path.GetRelativePath(root, path).Split(
                     new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, component);
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                return true;
            }
        }
        return false;
    }

    private void LaunchLegacy(string surface, string successMessage)
    {
        var executable = ResolveLegacyEnginePath();
        if (executable is null)
        {
            SetStatus($"{surface} is unavailable because the full WinCare Pro engine was not found.");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true });
            SetStatus(successMessage);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            SetStatus($"{surface} could not open. No care action was started.");
        }
    }

    private sealed record BridgePaths(string PythonPath, string ScriptPath);
    private sealed record CareProfileOption(string Id, string Title, string Recommendation);

    private enum CancellationRecordResult
    {
        NotNeeded,
        Recorded,
        NoTimeRemaining,
        Failed,
    }

    private sealed class OperationBudget : IDisposable
    {
        private readonly TimeSpan _limit;
        private readonly Stopwatch _clock;
        private readonly CancellationTokenSource _deadline;

        public OperationBudget(TimeSpan limit)
        {
            _limit = limit;
            _clock = Stopwatch.StartNew();
            _deadline = new CancellationTokenSource(limit);
        }

        public CancellationToken DeadlineToken => _deadline.Token;
        public TimeSpan Remaining => _limit - _clock.Elapsed is var remaining && remaining > TimeSpan.Zero
            ? remaining
            : TimeSpan.Zero;
        public bool DeadlineExpired => Remaining <= TimeSpan.Zero || _deadline.IsCancellationRequested;

        public void Dispose()
        {
            _deadline.Dispose();
            _clock.Stop();
        }
    }

    private sealed class RelayCommand(Action action) : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => action();
    }
}
