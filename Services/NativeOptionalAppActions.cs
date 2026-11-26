using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace WinCareDesktop.Services;

/// <summary>
/// Strictly bounded allowlist of signed, non-critical optional media, chat, and
/// game-launcher apps in the current user session. Unknowns are never targets.
/// </summary>
public sealed class NativeOptionalAppActions : IOptionalAppActions
{
    private sealed record KnownAppSpec(
        string ProcessName,
        string CanonicalApp,
        string ExecutableFile,
        string[] ExpectedPublishers,
        string Guidance,
        Func<string, bool> IsAllowedInstallPath);

    private static readonly KnownAppSpec[] Specs =
    {
        new(
            "vlc",
            "VLC media player",
            "vlc.exe",
            new[] { "VideoLAN" },
            "Verified optional media player; save playlists/recordings before closing.",
            IsValidVlcInstall),
        new(
            "Spotify",
            "Spotify",
            "Spotify.exe",
            new[] { "Spotify AB" },
            "Verified background music player; closing frees audio/CEF memory.",
            path => IsUnderStandardRoot(path, "Spotify", "Spotify.exe")),
        new(
            "Discord",
            "Discord",
            "Discord.exe",
            new[] { "Discord Inc." },
            "Verified chat client; leave active voice calls before closing.",
            path => IsUnderLocalAppDataPrefix(path, "Discord", "Discord.exe")),
        new(
            "steam",
            "Steam",
            "steam.exe",
            new[] { "Valve Corp.", "Valve" },
            "Verified game launcher; exit games and downloads before closing.",
            path => IsUnderProgramFiles(path, "Steam", "steam.exe")),
        new(
            "EpicGamesLauncher",
            "Epic Games Launcher",
            "EpicGamesLauncher.exe",
            new[] { "Epic Games, Inc.", "Epic Games" },
            "Verified game launcher; pause active updates before closing.",
            path => IsUnderProgramFilesSubtree(path, "Epic Games", "EpicGamesLauncher.exe")),
    };

    public bool ActionsAllowed => !SafetyMode.Enabled;
    public string UserSid => WindowsIdentity.GetCurrent().User?.Value ?? "";
    public int Session { get; } = Process.GetCurrentProcess().SessionId;

    public async Task<IReadOnlyList<OptionalAppCandidate>> Read(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var result = new List<OptionalAppCandidate>();

        foreach (var spec in Specs)
        {
            token.ThrowIfCancellationRequested();
            var processes = Process.GetProcessesByName(spec.ProcessName);
            try
            {
                foreach (var process in processes.Take(4))
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        var (identity, workingSetMb) = await InspectWithMemory(process, spec, token);
                        if (identity is not null && OptionalAppRules.Eligible(identity, UserSid, Session))
                        {
                            result.Add(new(identity, spec.Guidance, workingSetMb));
                        }
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception) { /* Unknown/inaccessible identity is excluded, never inferred safe. */ }
                }
            }
            finally
            {
                foreach (var process in processes) process.Dispose();
            }
        }

        return result.OrderByDescending(c => c.WorkingSetMb).ToArray();
    }

    public async Task<OptionalAppIdentity?> Revalidate(OptionalAppIdentity identity, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var spec = Specs.FirstOrDefault(s => s.CanonicalApp == identity.App);
        if (spec is null) return null;
        using var process = Process.GetProcessById(identity.Pid);
        var (verified, _) = await InspectWithMemory(process, spec, token);
        return verified;
    }

    public async Task<OptionalAppCloseResult> Close(OptionalAppIdentity identity, CancellationToken token)
    {
        SafetyMode.RequireMutationAllowed();
        token.ThrowIfCancellationRequested();
        var spec = Specs.FirstOrDefault(s => s.CanonicalApp == identity.App)
            ?? throw new InvalidOperationException("Unsupported optional app.");

        using var process = Process.GetProcessById(identity.Pid);
        // Open and retain a process handle before asynchronous checks, preventing PID-reuse substitution.
        _ = process.Handle;
        var path = process.MainModule?.FileName ?? throw new IOException("Unknown image.");
        using var image = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var (current, _) = await InspectWithMemory(process, spec, token);
        if (current != identity || !OptionalAppRules.Eligible(current!, UserSid, Session))
            throw new InvalidOperationException("Target changed; close refused.");

        token.ThrowIfCancellationRequested();
        process.Refresh();
        if (process.HasExited ||
            process.StartTime.ToUniversalTime().Ticks != identity.StartedUtcTicks ||
            process.MainWindowHandle.ToInt64() != identity.Window ||
            process.SessionId != identity.Session ||
            !string.Equals(process.MainModule?.FileName, identity.Path, StringComparison.OrdinalIgnoreCase) ||
            !WindowBelongs(process.MainWindowHandle, identity.Pid))
            throw new InvalidOperationException("Target changed; close refused.");

        SafetyMode.RequireMutationAllowed();
        token.ThrowIfCancellationRequested();
        // No Kill, no tree operation, no fallback. False is not a successful close request.
        if (!process.CloseMainWindow())
            return new(false, false, "Close request not sent: main window is unavailable or disabled. No force was used.");

        using var wait = CancellationTokenSource.CreateLinkedTokenSource(token);
        wait.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            await process.WaitForExitAsync(wait.Token);
            return new(true, true, "Verified process exit after graceful close. No RAM reduction or permanent startup change is claimed.");
        }
        catch (OperationCanceledException)
        {
            return new(true, false, "Graceful close requested; exit not verified. The app may need confirmation or may refuse. No force was used; check its window.");
        }
    }

    private async Task<(OptionalAppIdentity? Identity, double WorkingSetMb)> InspectWithMemory(
        Process process, KnownAppSpec spec, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        _ = process.Handle;
        process.Refresh();
        if (process.HasExited || process.SessionId != Session || process.MainWindowHandle == IntPtr.Zero || !WindowBelongs(process.MainWindowHandle, process.Id))
            return (null, 0);

        if (!OpenProcessToken(process.Handle, 8, out var owner))
            return (null, 0);

        string? sid;
        using (owner)
        {
            using var identity = new WindowsIdentity(owner.DangerousGetHandle());
            sid = identity.User?.Value;
            if (!GetTokenInformation(owner, 20, out int elevated, sizeof(int), out _) || elevated != 0)
                return (null, 0);
        }

        if (sid != UserSid || !IsProcessCritical(process.Handle, out var critical) || critical)
            return (null, 0);

        var path = process.MainModule?.FileName;
        if (path is null || !string.Equals(Path.GetFileName(path), spec.ExecutableFile, StringComparison.OrdinalIgnoreCase))
            return (null, 0);

        if (!spec.IsAllowedInstallPath(path))
            return (null, 0);

        PathSafety.RejectReparsePoints(path);
        using var image = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var version = FileVersionInfo.GetVersionInfo(path);
        if (spec.CanonicalApp == "VLC media player" &&
            (version.ProductName != "VLC media player" || !string.Equals(version.OriginalFilename, "vlc.exe", StringComparison.OrdinalIgnoreCase)))
            return (null, 0);

        var escaped = path.Replace("'", "''");
        var publishers = string.Join(",", spec.ExpectedPublishers.Select(p => "'" + p.Replace("'", "''") + "'"));
        var signature = await RescuePowerShell.RunAsync(
            "$s=Microsoft.PowerShell.Security\\Get-AuthenticodeSignature -LiteralPath '" + escaped + "'; " +
            "$cn=$s.SignerCertificate.GetNameInfo([System.Security.Cryptography.X509Certificates.X509NameType]::SimpleName,$false); " +
            "if($s.Status -ne 'Valid' -or @(" + publishers + ") -notcontains $cn){exit 2}; 'trusted-optional-app'",
            false, TimeSpan.FromSeconds(15), token);
        if (signature.ExitCode != 0 || signature.TimedOut || signature.Output.Trim() != "trusted-optional-app")
            return (null, 0);

        process.Refresh();
        if (process.HasExited || !WindowBelongs(process.MainWindowHandle, process.Id))
            return (null, 0);

        var mb = Math.Round(process.WorkingSet64 / 1_048_576.0, 1);
        var verified = new OptionalAppIdentity(
            process.Id,
            process.StartTime.ToUniversalTime().Ticks,
            path,
            sid!,
            process.SessionId,
            process.MainWindowHandle.ToInt64(),
            spec.CanonicalApp,
            true,
            false);
        return (verified, mb);
    }

    private static bool IsValidVlcInstall(string path)
    {
        var roots = new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) };
        if (!roots.Where(r => !string.IsNullOrWhiteSpace(r)).Any(r => string.Equals(path, Path.Combine(r, "VideoLAN", "VLC", "vlc.exe"), StringComparison.OrdinalIgnoreCase)))
            return false;

        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using var key = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\VLC media player", false);
            if (key?.GetValue("Publisher") is not string publisher || publisher != "VideoLAN") continue;
            if (key.GetValue("DisplayName") is not string name || name != "VLC media player") continue;
            if (key.GetValue("InstallLocation") is string install && Path.IsPathFullyQualified(install) &&
                string.Equals(Path.GetFullPath(Path.Combine(install, "vlc.exe")), path, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static bool IsUnderProgramFiles(string path, string folder, string exe)
    {
        var roots = new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) };
        return roots.Where(r => !string.IsNullOrWhiteSpace(r))
            .Any(r => string.Equals(Path.GetFullPath(path), Path.GetFullPath(Path.Combine(r, folder, exe)), StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsUnderProgramFilesSubtree(string path, string vendorFolder, string exe)
    {
        if (!string.Equals(Path.GetFileName(path), exe, StringComparison.OrdinalIgnoreCase)) return false;
        var roots = new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) };
        return roots.Where(r => !string.IsNullOrWhiteSpace(r))
            .Any(r => PathSafety.IsWithin(Path.GetFullPath(path), Path.Combine(r, vendorFolder)));
    }

    private static bool IsUnderLocalAppDataPrefix(string path, string vendorFolder, string exe)
    {
        if (!string.Equals(Path.GetFileName(path), exe, StringComparison.OrdinalIgnoreCase)) return false;
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return !string.IsNullOrWhiteSpace(local) && PathSafety.IsWithin(Path.GetFullPath(path), Path.Combine(local, vendorFolder));
    }

    private static bool IsUnderStandardRoot(string path, string vendorFolder, string exe)
    {
        if (!string.Equals(Path.GetFileName(path), exe, StringComparison.OrdinalIgnoreCase)) return false;
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return (!string.IsNullOrWhiteSpace(appData) && PathSafety.IsWithin(Path.GetFullPath(path), Path.Combine(appData, vendorFolder)))
            || (!string.IsNullOrWhiteSpace(local) && PathSafety.IsWithin(Path.GetFullPath(path), Path.Combine(local, "Microsoft", "WindowsApps")))
            || IsUnderProgramFiles(path, vendorFolder, exe);
    }

    private static bool WindowBelongs(IntPtr window, int pid) =>
        window != IntPtr.Zero && GetWindowThreadProcessId(window, out var owner) != 0 && owner == pid;

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out SafeAccessTokenHandle token);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(SafeAccessTokenHandle token, int kind, out int information, int length, out int returned);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool IsProcessCritical(IntPtr process, [MarshalAs(UnmanagedType.Bool)] out bool critical);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out int process);
}
