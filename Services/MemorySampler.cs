namespace WinCareDesktop.Services;

public sealed record ProcessSample(int Pid, long StartedUtcTicks, string Name, long WorkingSetBytes, double CpuMilliseconds, long CapturedTicks = 0);

public sealed record MemoryRow(int Pid, string Name, double MemoryMb, double? CpuPercent)
{
    public string CpuText => CpuPercent is { } cpu ? $"{cpu:F1}%" : "Unknown";
    public string RoleHint => ClassifyProcessRole(Name);
    public string IdentityText => $"PID {Pid} • {RoleHint} • manual review only";

    public static string ClassifyProcessRole(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return "Unknown role";
        var n = processName.Trim().ToLowerInvariant();
        if (n is "system" or "idle" or "registry" or "smss" or "csrss" or "wininit" or "services" or "lsass" or
            "svchost" or "dwm" or "winlogon" or "fontdrvhost" or "sihost" or "taskhostw" or "explorer" or
            "ctfmon" or "conhost" or "dllhost" or "audiodg" or "spoolsv" or "wmiprvse" or "searchindexer" or
            "searchhost" or "startmenuexperiencehost" or "shellexperiencehost" or "runtimebroker" or "textinputhost")
            return "Windows System — keep running";

        if (n is "msmpeng" or "mpdefendercoreservice" or "nissrv" or "securityhealthservice" or "securityhealthsystray" or "smartscreen")
            return "Windows Security — keep running";

        if (n is "chrome" or "msedge" or "msedgewebview2" or "firefox" or "brave" or "opera" or "vivaldi" or "iexplore")
            return "Browser / WebView — close unused tabs";

        if (n is "discord" or "teams" or "ms-teams" or "slack" or "zoom" or "telegram" or "whatsapp" or "skype")
            return "Communication / Chat";

        if (n is "steam" or "steamwebhelper" or "epicgameslauncher" or "battle.net" or "riotclientservices" or "galaxyclient" or "ea desktop")
            return "Game / Launcher";

        if (n is "vlc" or "spotify" or "wmplayer" or "music.ui" or "video.ui" or "obs64" or "audacity")
            return "Media / Audio";

        if (n is "code" or "devenv" or "dotnet" or "node" or "python" or "pythonw" or "java" or "javaw" or "powershell" or "pwsh" or "windowsterminal")
            return "Developer / Shell";

        return "User or background process";
    }
}

public static class MemorySampler
{
    public static IReadOnlyList<MemoryRow> Compare(IReadOnlyList<ProcessSample> before, IReadOnlyList<ProcessSample> after, TimeSpan elapsed, int processors)
    {
        return after.OrderByDescending(p => p.WorkingSetBytes).Take(20).Select(p =>
        {
            var previous = before.FirstOrDefault(x => x.Pid == p.Pid && x.StartedUtcTicks == p.StartedUtcTicks);
            var delta = previous is null ? double.NaN : p.CpuMilliseconds - previous.CpuMilliseconds;
            var interval = previous is not null && previous.CapturedTicks > 0 && p.CapturedTicks > 0
                ? (p.CapturedTicks - previous.CapturedTicks) * 1000d / System.Diagnostics.Stopwatch.Frequency : elapsed.TotalMilliseconds;
            double? cpu = interval > 0 && processors > 0 && double.IsFinite(delta) && delta >= 0
                ? Math.Clamp(delta / interval / processors * 100, 0, 100) : null;
            return new MemoryRow(p.Pid, p.Name, p.WorkingSetBytes / 1048576d, cpu);
        }).ToArray();
    }
}
