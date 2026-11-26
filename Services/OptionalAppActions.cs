namespace WinCareDesktop.Services;

public sealed record OptionalAppIdentity(
    int Pid,
    long StartedUtcTicks,
    string Path,
    string OwnerSid,
    int Session,
    long Window,
    string App,
    bool TrustedInstall,
    bool ProtectedRole);

public sealed record OptionalAppCandidate(OptionalAppIdentity Identity, string Reason, double WorkingSetMb = 0)
{
    public string Name => Identity.App;
    public string Detail => WorkingSetMb > 0
        ? $"PID {Identity.Pid} • {WorkingSetMb:F1} MB RAM • {Reason}"
        : $"PID {Identity.Pid} • {Reason}";
}

public static class OptionalAppRules
{
    public static readonly IReadOnlySet<string> AllowedApps = new HashSet<string>(StringComparer.Ordinal)
    {
        "VLC media player",
        "Spotify",
        "Discord",
        "Steam",
        "Epic Games Launcher",
    };

    public static bool Eligible(OptionalAppIdentity identity, string user, int session) =>
        AllowedApps.Contains(identity.App) &&
        identity.TrustedInstall &&
        !identity.ProtectedRole &&
        identity.Pid > 0 &&
        identity.StartedUtcTicks > 0 &&
        !string.IsNullOrWhiteSpace(identity.Path) &&
        !string.IsNullOrWhiteSpace(user) &&
        identity.OwnerSid == user &&
        identity.Session == session &&
        identity.Window != 0;
}

public sealed record OptionalAppCloseResult(bool RequestSent, bool ExitVerified, string Message);

public interface IOptionalAppActions
{
    bool ActionsAllowed { get; }
    string UserSid { get; }
    int Session { get; }
    Task<IReadOnlyList<OptionalAppCandidate>> Read(CancellationToken token);
    Task<OptionalAppIdentity?> Revalidate(OptionalAppIdentity identity, CancellationToken token);
    Task<OptionalAppCloseResult> Close(OptionalAppIdentity identity, CancellationToken token);
}
