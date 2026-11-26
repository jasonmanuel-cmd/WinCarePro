using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Windows;
using WinCareDesktop.Services;

namespace WinCareDesktop.ViewModels;

public partial class MemoryViewModel
{
    private readonly IOptionalAppActions _apps;

    public MemoryViewModel(IOptionalAppActions apps)
    {
        _apps = apps;
        Title = "Memory & Apps";
        Subtitle = "Measured demand • individually confirmed optional-app relief";
    }

    [ObservableProperty] private IReadOnlyList<OptionalAppCandidate> _optionalApps = Array.Empty<OptionalAppCandidate>();
    [ObservableProperty] private string _appStatus = "No eligible-app review yet. Only Authenticode-verified optional apps (VLC, Spotify, Discord, Steam, Epic Games Launcher) in your session can qualify; all other apps remain manual.";
    [ObservableProperty] private bool _appBusy;

    private CancellationTokenSource? _appCancel;
    public bool CanCloseApps => _apps.ActionsAllowed && !AppBusy;
    public Action<string, string> Audit { get; set; } = new RescueAudit().Record;

    public Func<OptionalAppIdentity, bool> ConfirmClose { get; set; } = app =>
        MessageBox.Show(
            $"Close {app.App} (PID {app.Pid})?\n\n" +
            $"Verified image: {app.Path}\n" +
            $"Started: {new DateTime(app.StartedUtcTicks, DateTimeKind.Utc).ToLocalTime():g}\n\n" +
            "Save playlists, calls, downloads and other work first. This requests an ordinary window close, not a force-kill; the app can refuse or ask you to save. Closing is not automatically reversible. No startup setting is changed.",
            "Confirm this app only",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning) == MessageBoxResult.Yes;

    partial void OnAppBusyChanged(bool value) => OnPropertyChanged(nameof(CanCloseApps));

    [RelayCommand]
    public async Task ReviewAppsAsync()
    {
        if (AppBusy) return;
        AppBusy = true;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        _appCancel = cts;
        try
        {
            var rows = await _apps.Read(cts.Token);
            cts.Token.ThrowIfCancellationRequested();
            OptionalApps = rows.Where(r => OptionalAppRules.Eligible(r.Identity, _apps.UserSid, _apps.Session)).ToArray();
            AppStatus = $"{OptionalApps.Count} verified optional app(s) found. Bounded review checks only signed VLC, Spotify, Discord, Steam, and Epic Games Launcher windows in your session. Unknown, unsigned, or protected processes are omitted. Safe mode disables close requests.";
        }
        catch (OperationCanceledException)
        {
            OptionalApps = Array.Empty<OptionalAppCandidate>();
            AppStatus = "Eligible-app review cancelled or timed out. No apps closed.";
        }
        catch (Exception)
        {
            OptionalApps = Array.Empty<OptionalAppCandidate>();
            AppStatus = "Eligible-app review incomplete or unavailable. No apps closed; use their own windows.";
        }
        finally
        {
            _appCancel = null;
            AppBusy = false;
        }
    }

    [RelayCommand]
    public async Task CloseAppAsync(OptionalAppCandidate? candidate)
    {
        if (!CanCloseApps || candidate is null || !OptionalApps.Contains(candidate) || !OptionalAppRules.Eligible(candidate.Identity, _apps.UserSid, _apps.Session))
        {
            AppStatus = "Close blocked: safe mode, unknown target, or no current eligible-app review.";
            return;
        }

        AppBusy = true;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        _appCancel = cts;
        var requested = false;
        var auditLabel = $"{candidate.Identity.App} graceful close";
        try
        {
            if (!ConfirmClose(candidate.Identity))
            {
                AppStatus = "Close declined; app retained.";
                return;
            }
            cts.Token.ThrowIfCancellationRequested();
            var fresh = await _apps.Revalidate(candidate.Identity, cts.Token);
            cts.Token.ThrowIfCancellationRequested();
            if (fresh != candidate.Identity || !OptionalAppRules.Eligible(fresh!, _apps.UserSid, _apps.Session))
            {
                AppStatus = "Close refused: PID, start time, image, owner, session, window or trust changed. Review again.";
                return;
            }
            if (!_apps.ActionsAllowed) throw new InvalidOperationException();
            await Task.Run(() => Audit(auditLabel, "Confirmed request"));
            cts.Token.ThrowIfCancellationRequested();
            requested = true;
            var result = await _apps.Close(fresh!, cts.Token);
            AppStatus = result.Message;
            Audit(auditLabel, result.ExitVerified ? "Exit verified" : result.RequestSent ? "Requested; exit unverified" : "Request not sent");
        }
        catch (OperationCanceledException)
        {
            AppStatus = requested
                ? "Stopped waiting; a close request may already have been delivered. Check the app. No force used."
                : "Close preparation cancelled; app retained.";
        }
        catch (Exception)
        {
            AppStatus = requested
                ? "Close outcome or audit unverified. Check the app; no force fallback was attempted."
                : "Close refused: identity, permissions or audit could not be verified. App retained.";
        }
        finally
        {
            OptionalApps = OptionalApps.Where(x => x != candidate).ToArray();
            _appCancel = null;
            AppBusy = false;
        }
    }

    [RelayCommand] public void CancelAppReview() => _appCancel?.Cancel();
}
