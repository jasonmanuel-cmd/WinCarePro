using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using Microsoft.Win32;
using WinCareDesktop.Models;

namespace WinCareDesktop.Services;

public enum SecurityFindingLevel
{
    Clean = 0,
    Review = 1,
    Warning = 2,
}

public sealed record SecurityAuditFinding(
    string Category,
    SecurityFindingLevel Level,
    string Title,
    string Detail)
{
    public string BadgeText => Level switch
    {
        SecurityFindingLevel.Warning => "WARNING",
        SecurityFindingLevel.Review => "REVIEW",
        _ => "NOMINAL"
    };
}

public sealed record SecurityAuditReport(
    DateTimeOffset CheckedAt,
    IReadOnlyList<SecurityAuditFinding> Findings)
{
    public int WarningCount => Findings.Count(f => f.Level == SecurityFindingLevel.Warning);
    public int ReviewCount => Findings.Count(f => f.Level == SecurityFindingLevel.Review);
    public string Summary => WarningCount > 0
        ? $"Audit found {WarningCount} high-priority warning(s) and {ReviewCount} review item(s). Inspect details below."
        : ReviewCount > 0
            ? $"No critical hijack warnings; {ReviewCount} item(s) flagged for manual review."
            : $"All {Findings.Count} persistence, proxy & hosts checks reported nominal.";
}

public sealed record FileTriageReport(
    string FullPath,
    long SizeBytes,
    string Sha256,
    string SignatureStatus,
    string SignerName,
    bool HasMarkOfTheWeb,
    string Assessment);

/// <summary>
/// Read-only Malwarebytes AdwCleaner + Sysinternals Autoruns + HitmanPro style
/// persistence, hijack &amp; file-signature inspector, plus confirmed targeted
/// Microsoft Defender single-file scanning.
/// </summary>
public sealed class SecurityThreatAuditService
{
    private static readonly string[] HighValueRedirectDomains =
    {
        "microsoft.com", "windowsupdate.com", "update.microsoft.com", "defender",
        "virustotal.com", "malwarebytes.com", "kaspersky", "bitdefender", "eset.com",
        "Norton", "avast", "google.com", "bing.com", "github.com", "paypal.com"
    };

    private static readonly string[] SuspiciousLolBins =
    {
        "mshta", "wscript", "cscript", "regsvr32", "certutil", "bitsadmin",
        "-encodedcommand", " -enc ", "frombase64string", "invoke-expression", " iex(",
        "-windowstyle hidden", " -w hidden", "rundll32 javascript:"
    };

    public sealed record OsSecurityPosture(
        bool? FirewallPrivateEnabled,
        bool? FirewallPublicEnabled,
        bool? UacEnabled,
        string? SmartScreenMode,
        bool? SecureBootEnabled);

    public Func<string?>? ReadHostsContent { get; set; }
    public Func<(bool ProxyEnabled, string? ProxyServer, string? AutoConfigUrl)>? ReadProxySettings { get; set; }
    public Func<IReadOnlyList<(string Source, string Name, string Command)>>? ReadAutorunCommands { get; set; }
    public Func<OsSecurityPosture>? ReadSecurityPosture { get; set; }

    public Task<SecurityAuditReport> RunAuditAsync(CancellationToken token = default)
    {
        return Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            var findings = new List<SecurityAuditFinding>();

            AuditHostsFile(findings);
            token.ThrowIfCancellationRequested();

            AuditProxySettings(findings);
            token.ThrowIfCancellationRequested();

            AuditStartupPersistence(findings);
            token.ThrowIfCancellationRequested();

            AuditOsSecurityPosture(findings);
            return new SecurityAuditReport(DateTimeOffset.Now, findings);
        }, token);
    }

    public static IReadOnlyList<SecurityAuditFinding> EvaluateSecurityPosture(OsSecurityPosture p)
    {
        var list = new List<SecurityAuditFinding>(4);

        // 1. Windows Firewall (Private + Public profiles)
        if (p.FirewallPrivateEnabled == false || p.FirewallPublicEnabled == false)
        {
            var disabled = new List<string>();
            if (p.FirewallPrivateEnabled == false) disabled.Add("Private");
            if (p.FirewallPublicEnabled == false) disabled.Add("Public");
            list.Add(new("FIREWALL", SecurityFindingLevel.Warning,
                $"Windows Firewall disabled ({string.Join(" & ", disabled)} profile)",
                "Malware and remote-access trojans frequently turn off Windows Firewall profiles. Re-enable in Windows Security → Firewall & network protection."));
        }
        else if (p.FirewallPrivateEnabled == true && p.FirewallPublicEnabled == true)
        {
            list.Add(new("FIREWALL", SecurityFindingLevel.Clean,
                "Windows Firewall active (Private & Public)",
                "Both Private and Public network firewall profiles report active filtering."));
        }
        else
        {
            list.Add(new("FIREWALL", SecurityFindingLevel.Review,
                "Windows Firewall policy state unknown",
                "Could not verify both firewall profile registry flags; check Windows Security → Firewall."));
        }

        // 2. User Account Control (EnableLUA)
        if (p.UacEnabled == false)
        {
            list.Add(new("UAC", SecurityFindingLevel.Warning,
                "User Account Control (EnableLUA) is disabled",
                "Silent privilege escalation risk: UAC is disabled in HKLM Policies\\System. Malware disables EnableLUA to gain silent admin rights."));
        }
        else if (p.UacEnabled == true)
        {
            list.Add(new("UAC", SecurityFindingLevel.Clean,
                "User Account Control (UAC) active",
                "EnableLUA is active so administrative changes require explicit consent."));
        }
        else
        {
            list.Add(new("UAC", SecurityFindingLevel.Review,
                "UAC state unreadable",
                "Could not read EnableLUA policy value."));
        }

        // 3. SmartScreen Reputation Check
        if (string.Equals(p.SmartScreenMode, "Off", StringComparison.OrdinalIgnoreCase))
        {
            list.Add(new("SMARTSCREEN", SecurityFindingLevel.Warning,
                "Windows SmartScreen reputation check is Off",
                "Explorer SmartScreen is turned off, reducing protection against untrusted downloaded installers and phishing payloads."));
        }
        else if (!string.IsNullOrWhiteSpace(p.SmartScreenMode))
        {
            list.Add(new("SMARTSCREEN", SecurityFindingLevel.Clean,
                $"SmartScreen active ({p.SmartScreenMode})",
                "Explorer SmartScreen reputation screening is enabled for downloaded executables."));
        }
        else
        {
            list.Add(new("SMARTSCREEN", SecurityFindingLevel.Clean,
                "SmartScreen default policy",
                "No SmartScreen disable override detected."));
        }

        // 4. UEFI Secure Boot
        if (p.SecureBootEnabled == true)
        {
            list.Add(new("SECUREBOOT", SecurityFindingLevel.Clean,
                "UEFI Secure Boot enabled",
                "Hardware boot chain verifies signed OS loaders against pre-boot rootkits/bootkits."));
        }
        else if (p.SecureBootEnabled == false)
        {
            list.Add(new("SECUREBOOT", SecurityFindingLevel.Review,
                "UEFI Secure Boot not enabled",
                "Secure Boot reports off (common on legacy BIOS, VMs, or dual-boot setups; enable in UEFI firmware if supported)."));
        }

        return list;
    }

    private void AuditOsSecurityPosture(List<SecurityAuditFinding> findings)
    {
        try
        {
            var posture = ReadSecurityPosture != null ? ReadSecurityPosture() : ReadSystemSecurityPosture();
            findings.AddRange(EvaluateSecurityPosture(posture));
        }
        catch
        {
            findings.Add(new("POSTURE", SecurityFindingLevel.Review, "OS security posture partially unreadable", "Could not read all HKLM security posture registry keys."));
        }
    }

    private static OsSecurityPosture ReadSystemSecurityPosture()
    {
        bool? ReadDwordBool(string subKey, string valueName)
        {
            try
            {
                using var k = Registry.LocalMachine.OpenSubKey(subKey, false);
                return k?.GetValue(valueName) is int v ? v != 0 : null;
            }
            catch { return null; }
        }

        string? smartScreen = null;
        try
        {
            using var exp = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer", false);
            smartScreen = exp?.GetValue("SmartScreenEnabled") as string;
        }
        catch { }

        return new OsSecurityPosture(
            FirewallPrivateEnabled: ReadDwordBool(@"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\StandardProfile", "EnableFirewall"),
            FirewallPublicEnabled: ReadDwordBool(@"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\PublicProfile", "EnableFirewall"),
            UacEnabled: ReadDwordBool(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "EnableLUA"),
            SmartScreenMode: smartScreen,
            SecureBootEnabled: ReadDwordBool(@"SYSTEM\CurrentControlSet\Control\SecureBoot\State", "UEFISecureBootEnabled"));
    }

    public static SecurityAuditFinding EvaluateHostsText(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return new("HOSTS", SecurityFindingLevel.Clean, "Hosts file nominal", "No custom domain redirections present in drivers\\etc\\hosts.");
        }

        var customEntries = new List<(string Ip, string Host)>();
        foreach (var rawLine in content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var commentIdx = rawLine.IndexOf('#');
            var line = (commentIdx >= 0 ? rawLine[..commentIdx] : rawLine).Trim();
            if (line.Length == 0) continue;

            var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;
            var ip = parts[0];
            foreach (var host in parts.Skip(1))
            {
                if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                    host.Equals("localhost.localdomain", StringComparison.OrdinalIgnoreCase) ||
                    host.Equals("local", StringComparison.OrdinalIgnoreCase) ||
                    host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
                    host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase) ||
                    host.Equals("broadcasthost", StringComparison.OrdinalIgnoreCase) ||
                    host.Contains("docker.internal", StringComparison.OrdinalIgnoreCase) ||
                    host.Contains("kubernetes.docker.internal", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                customEntries.Add((ip, host));
            }
        }

        if (customEntries.Count == 0)
        {
            return new("HOSTS", SecurityFindingLevel.Clean, "Hosts file nominal", "No custom external domain overrides found in drivers\\etc\\hosts.");
        }

        var hijackedSecurity = customEntries
            .Where(e => HighValueRedirectDomains.Any(d => e.Host.Contains(d, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (hijackedSecurity.Count > 0)
        {
            var sample = string.Join(", ", hijackedSecurity.Take(4).Select(x => $"{x.Host} -> {x.Ip}"));
            return new("HOSTS", SecurityFindingLevel.Warning,
                $"Security / update domain override in hosts ({hijackedSecurity.Count})",
                $"Found hosts redirection(s) affecting security or major domains ({sample}). Malware often blocks AV/Windows updates here.");
        }

        var nonLoopback = customEntries
            .Where(e => e.Ip is not "0.0.0.0" and not "127.0.0.1" and not "::1")
            .ToList();

        if (nonLoopback.Count > 0)
        {
            var sample = string.Join(", ", nonLoopback.Take(3).Select(x => $"{x.Host} -> {x.Ip}"));
            return new("HOSTS", SecurityFindingLevel.Review,
                $"Custom IP redirections in hosts ({nonLoopback.Count})",
                $"Non-loopback hosts override(s) detected ({sample}). Confirm you or a developer tool added them.");
        }

        return new("HOSTS", SecurityFindingLevel.Clean,
            $"Hosts file has {customEntries.Count} local blocklist entry(s)",
            "Only 0.0.0.0 / 127.0.0.1 ad-block or local sinkhole entries found; no external IP hijacks.");
    }

    public static SecurityAuditFinding EvaluateProxyState(bool enabled, string? proxyServer, string? autoConfigUrl)
    {
        if (!string.IsNullOrWhiteSpace(autoConfigUrl))
        {
            return new("PROXY", SecurityFindingLevel.Warning,
                "Automatic Proxy Configuration (PAC) URL active",
                $"Internet Settings points to AutoConfigURL '{autoConfigUrl}'. Verify this belongs to your organization/VPN; adware frequently uses PAC scripts to intercept traffic.");
        }

        if (enabled && !string.IsNullOrWhiteSpace(proxyServer))
        {
            return new("PROXY", SecurityFindingLevel.Review,
                $"System HTTP proxy enabled ({proxyServer})",
                "Current-user WinINET proxy is enabled. Normal if you run a VPN/debugging proxy; investigate if unexpected.");
        }

        return new("PROXY", SecurityFindingLevel.Clean,
            "System proxy & PAC nominal",
            "No WinINET proxy override or rogue PAC script URL configured for the current user.");
    }

    public static IReadOnlyList<SecurityAuditFinding> EvaluateAutorunEntries(
        IReadOnlyList<(string Source, string Name, string Command)> entries)
    {
        var list = new List<SecurityAuditFinding>();
        var suspiciousCount = 0;

        foreach (var (source, name, cmd) in entries)
        {
            if (string.IsNullOrWhiteSpace(cmd)) continue;
            var lower = cmd.ToLowerInvariant();

            if (SuspiciousLolBins.Any(lol => lower.Contains(lol, StringComparison.Ordinal)))
            {
                suspiciousCount++;
                list.Add(new("AUTORUN", SecurityFindingLevel.Warning,
                    $"Script / LOLBin startup command: {name}",
                    $"{source} launches script/interpreter syntax ({Truncate(cmd, 110)}). Malware commonly persists via encoded PowerShell, mshta, or wscript."));
                continue;
            }

            if (lower.Contains(@"\appdata\local\temp\") ||
                lower.Contains(@"\downloads\") ||
                lower.Contains(@"\users\public\"))
            {
                suspiciousCount++;
                list.Add(new("AUTORUN", SecurityFindingLevel.Warning,
                    $"Startup launching from transient folder: {name}",
                    $"{source} points inside Temp, Downloads, or Public ({Truncate(cmd, 110)}). Legitimate installed software rarely auto-starts from drop folders."));
            }
        }

        if (suspiciousCount == 0)
        {
            list.Add(new("AUTORUN", SecurityFindingLevel.Clean,
                $"Startup persistence audit nominal ({entries.Count} checked)",
                "No encoded PowerShell, script LOLBins, or %TEMP% / Downloads drop-folder persistence detected in Run, RunOnce, or Startup folders."));
        }

        return list;
    }

    private void AuditHostsFile(List<SecurityAuditFinding> findings)
    {
        try
        {
            string? text;
            if (ReadHostsContent != null)
            {
                text = ReadHostsContent();
            }
            else
            {
                var hostsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"drivers\etc\hosts");
                text = File.Exists(hostsPath) ? File.ReadAllText(hostsPath) : null;
            }
            findings.Add(EvaluateHostsText(text));
        }
        catch
        {
            findings.Add(new("HOSTS", SecurityFindingLevel.Review, "Hosts file unreadable", "Could not read System32\\drivers\\etc\\hosts; check permissions."));
        }
    }

    private void AuditProxySettings(List<SecurityAuditFinding> findings)
    {
        try
        {
            (bool enabled, string? server, string? pac) state;
            if (ReadProxySettings != null)
            {
                state = ReadProxySettings();
            }
            else
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings", false);
                var enableVal = key?.GetValue("ProxyEnable");
                var enabled = enableVal is int n && n != 0;
                var server = key?.GetValue("ProxyServer") as string;
                var pac = key?.GetValue("AutoConfigURL") as string;
                state = (enabled, server, pac);
            }
            findings.Add(EvaluateProxyState(state.enabled, state.server, state.pac));
        }
        catch
        {
            findings.Add(new("PROXY", SecurityFindingLevel.Review, "Proxy settings unreadable", "Could not inspect current-user Internet Settings registry key."));
        }
    }

    private void AuditStartupPersistence(List<SecurityAuditFinding> findings)
    {
        try
        {
            var entries = ReadAutorunCommands != null ? ReadAutorunCommands() : ReadSystemAutoruns();
            findings.AddRange(EvaluateAutorunEntries(entries));
        }
        catch
        {
            findings.Add(new("AUTORUN", SecurityFindingLevel.Review, "Startup persistence partially unreadable", "Some startup keys could not be enumerated."));
        }
    }

    private static IReadOnlyList<(string Source, string Name, string Command)> ReadSystemAutoruns()
    {
        var list = new List<(string, string, string)>();
        var hives = new (RegistryKey Root, string Label)[]
        {
            (Registry.CurrentUser, "HKCU"),
            (Registry.LocalMachine, "HKLM"),
        };
        var subKeys = new[]
        {
            @"Software\Microsoft\Windows\CurrentVersion\Run",
            @"Software\Microsoft\Windows\CurrentVersion\RunOnce",
        };

        foreach (var (root, label) in hives)
        {
            foreach (var sub in subKeys)
            {
                try
                {
                    using var key = root.OpenSubKey(sub, false);
                    if (key == null) continue;
                    foreach (var name in key.GetValueNames())
                    {
                        if (key.GetValue(name) is string cmd)
                            list.Add(($"{label}\\{Path.GetFileName(sub)}", name, cmd));
                    }
                }
                catch { }
            }
        }

        foreach (var folder in new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Startup),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup),
        }.Where(f => !string.IsNullOrWhiteSpace(f) && Directory.Exists(f)))
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(folder))
                {
                    if (Path.GetFileName(file).Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                    list.Add(("Startup Folder", Path.GetFileName(file), file));
                }
            }
            catch { }
        }

        return list;
    }

    public async Task<FileTriageReport> InspectFileAsync(string filePath, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var full = Path.GetFullPath(filePath);
        PathSafety.RejectReparsePoints(full);
        var info = new FileInfo(full);
        if (!info.Exists) throw new FileNotFoundException("Selected file does not exist.", full);

        string sha256;
        await using (var fs = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 65536, useAsync: true))
        {
            var hash = await SHA256.HashDataAsync(fs, token);
            sha256 = Convert.ToHexString(hash).ToLowerInvariant();
        }

        var hasMotw = File.Exists(full + ":Zone.Identifier");
        var escaped = full.Replace("'", "''");
        var sigCmd = await RescuePowerShell.RunAsync(
            "$s=Microsoft.PowerShell.Security\\Get-AuthenticodeSignature -LiteralPath '" + escaped + "'; " +
            "$cn=if($s.SignerCertificate){$s.SignerCertificate.GetNameInfo([System.Security.Cryptography.X509Certificates.X509NameType]::SimpleName,$false)}else{'None'}; " +
            "[Console]::WriteLine(\"$($s.Status)|$cn\")",
            mutation: false,
            timeout: TimeSpan.FromSeconds(15),
            token: token);

        var sigStatus = "Unknown";
        var signer = "None";
        if (!sigCmd.TimedOut && sigCmd.ExitCode == 0)
        {
            var line = sigCmd.Output.Trim().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "";
            var split = line.Split('|', 2);
            if (split.Length == 2)
            {
                sigStatus = split[0].Trim();
                signer = split[1].Trim();
            }
        }

        var assessment = sigStatus switch
        {
            "Valid" => $"Valid Authenticode signature ({signer})" + (hasMotw ? " • Downloaded from internet (Mark-of-the-Web present)." : " • Local file."),
            "NotSigned" => "Unsigned file" + (hasMotw ? " downloaded from the internet — scan with Microsoft Defender before opening!" : " — verify origin or scan with Defender before running."),
            "HashMismatch" => "TAMPERED SIGNATURE (HashMismatch) — Do NOT run this file; scan or quarantine immediately!",
            _ => $"Signature status: {sigStatus} ({signer}). Run a targeted Defender scan if untrusted."
        };

        return new FileTriageReport(full, info.Length, sha256, sigStatus, signer, hasMotw, assessment);
    }

    public async Task<string> ScanFileWithDefenderAsync(string filePath, CancellationToken token = default)
    {
        SafetyMode.RequireMutationAllowed();
        token.ThrowIfCancellationRequested();
        var full = Path.GetFullPath(filePath);
        PathSafety.RejectReparsePoints(full);
        if (!File.Exists(full)) return "Target file no longer exists; no scan ran.";

        var mpCmdRun = ResolveMpCmdRunPath();
        if (mpCmdRun is null)
            return "Microsoft Defender CLI (MpCmdRun.exe) was not found on this Windows build. Scan the file via right-click → Scan with Microsoft Defender.";

        var psi = new ProcessStartInfo
        {
            FileName = mpCmdRun,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add("-Scan");
        psi.ArgumentList.Add("-ScanType");
        psi.ArgumentList.Add("3");
        psi.ArgumentList.Add("-File");
        psi.ArgumentList.Add(full);

        using var proc = Process.Start(psi) ?? throw new IOException("Could not start MpCmdRun.exe.");
        var stdOutTask = proc.StandardOutput.ReadToEndAsync(token);
        var stdErrTask = proc.StandardError.ReadToEndAsync(token);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(90));
        await proc.WaitForExitAsync(timeoutCts.Token);
        var output = (await stdOutTask + "\n" + await stdErrTask).Trim();

        return proc.ExitCode switch
        {
            0 => $"Defender targeted scan finished (exit 0): no threats detected in '{Path.GetFileName(full)}'.",
            2 => $"ALERT: Microsoft Defender reported a threat in '{Path.GetFileName(full)}' (exit 2)! Open Windows Security → Protection history immediately.",
            _ => $"Defender scan returned code {proc.ExitCode}. Output: {Truncate(output, 160)}"
        };
    }

    private static string? ResolveMpCmdRunPath()
    {
        var progData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        if (!string.IsNullOrWhiteSpace(progData))
        {
            var platformDir = Path.Combine(progData, @"Microsoft\Windows Defender\Platform");
            if (Directory.Exists(platformDir))
            {
                var latest = Directory.GetDirectories(platformDir)
                    .OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase)
                    .Select(d => Path.Combine(d, "MpCmdRun.exe"))
                    .FirstOrDefault(File.Exists);
                if (latest != null) return latest;
            }
        }

        var progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var fallback = Path.Combine(progFiles, @"Windows Defender\MpCmdRun.exe");
        return File.Exists(fallback) ? fallback : null;
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max] + "…";
}
