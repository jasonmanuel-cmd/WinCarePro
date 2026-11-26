using System.Management;
using System.Runtime.InteropServices;
using System.IO;
using WinCareDesktop.Models;

namespace WinCareDesktop.Services;

public class SystemOptimizerService
{
    private readonly List<string> _logStream = new();
    private readonly HistoryStore _history;

    /// <summary>
    /// The history store can be redirected so tests never write to the real
    /// %LOCALAPPDATA%\WinCarePro\history.json. Without this, running the test
    /// suite appended its own transactions to the user's actual undo history.
    /// </summary>
    private readonly Func<string, int?> _readPrivacy;
    private readonly Action<string, int?> _writePrivacy;
    public SystemOptimizerService(HistoryStore? history = null, Func<string, int?>? readPrivacy = null, Action<string, int?>? writePrivacy = null)
    {
        _history = history ?? new HistoryStore();
        _readPrivacy = readPrivacy ?? ReadPrivacyValue;
        _writePrivacy = writePrivacy ?? WritePrivacyValue;
    }

    public string HistoryFilePath => _history.FilePath;

    public IReadOnlyList<string> LogStream => _logStream.AsReadOnly();

    public void Log(string message, string type = "system")
    {
        var entry = $"[{DateTime.Now:HH:mm:ss}] [{type.ToUpper()}] {message}";
        _logStream.Add(entry);
        if (_logStream.Count > 200) _logStream.RemoveAt(0);
    }

    // ── RAM ──────────────────────────────────────────────────────────────
    public (double usedPercent, long totalBytes, long availableBytes) GetRamStats()
    {
        try
        {
            // GlobalMemoryStatusEx reads physical RAM directly from the kernel in
            // ~2 microseconds without waking WmiPrvSE.exe or allocating COM objects.
            var mem = new NativeMethods.MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<NativeMethods.MEMORYSTATUSEX>() };
            if (NativeMethods.GlobalMemoryStatusEx(ref mem) && mem.ullTotalPhys > 0)
            {
                var total = (long)Math.Min(mem.ullTotalPhys, long.MaxValue);
                var free = (long)Math.Min(mem.ullAvailPhys, (ulong)total);
                var usedPct = (double)(total - free) / total * 100.0;
                return (Math.Round(usedPct, 1), total, free);
            }

            using var mc = new ManagementObjectSearcher("SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem");
            foreach (var mo in mc.Get())
            {
                using (mo)
                {
                    var total = Convert.ToInt64(mo["TotalVisibleMemorySize"]) * 1024;
                    var free  = Convert.ToInt64(mo["FreePhysicalMemory"]) * 1024;
                    var usedPct = total > 0 ? (double)(total - free) / total * 100 : 0;
                    return (Math.Round(usedPct, 1), total, free);
                }
            }
        }
        catch (Exception ex) { Log($"RAM query failed: {ex.Message}", "warning"); }
        return (0, 0, 0);
    }

    public double GetBatteryPercent()
    {
        try
        {
            var mos = new ManagementObjectSearcher("SELECT EstimatedChargeRemaining FROM Win32_Battery");
            foreach (var mo in mos.Get())
            {
                if (mo["EstimatedChargeRemaining"] != null)
                    return Convert.ToDouble(mo["EstimatedChargeRemaining"]);
            }
        }
        catch (Exception ex) { Log($"Battery query failed: {ex.Message}", "warning"); }
        return -1;
    }

    public void FlushDnsCache()
    {
        SafetyMode.RequireMutationAllowed();
        var (code, _, err) = RunProcess("ipconfig", "/flushdns");
        Log(code == 0 ? "DNS cache flushed." : $"DNS cache flush failed: {err}".Trim(), code == 0 ? "success" : "warning");
    }

    public static bool IsElevated()
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        var principal = new System.Security.Principal.WindowsPrincipal(identity);
        return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }

    public Func<bool>? WorkingSetTrimmer { get; set; }
    public void FlushStandbyMemory()
    {
        try
        {
            bool success;
            if (WorkingSetTrimmer != null) success = WorkingSetTrimmer();
            else
            {
                SafetyMode.RequireMutationAllowed();
                success = NativeMethods.SetProcessWorkingSetSize(NativeMethods.GetCurrentProcess(), new IntPtr(-1), new IntPtr(-1));
            }
            Log(success ? "Trimmed WinCare's own working set. This does not flush the system standby list or create memory." : "Working-set trim failed.", success ? "success" : "warning");
        }
        catch (Exception ex) { Log("Working-set trim failed: " + ex.Message, "warning"); }
    }

    /// <summary>
    /// Trims one process's working set, forcing its resident pages back to the
    /// standby list.
    /// </summary>
    /// <remarks>
    /// The honest framing matters: this does not create memory. It moves pages
    /// out of the active set so a program that was paging can fault them back
    /// faster, which is why it can help responsiveness and why the free-memory
    /// figure barely moves. Anything that looks like it manufactures free RAM
    /// is lying about what the memory manager does.
    /// </remarks>
    public bool TrimProcessWorkingSet(int pid)
    {
        SafetyMode.RequireMutationAllowed();
        IntPtr handle = IntPtr.Zero;
        try
        {
            handle = OpenProcess(0x0008 | 0x0010 | 0x0400, false, pid); // VM_READ|VM_WRITE|QUERY
            if (handle == IntPtr.Zero) return false;

            return NativeMethods.SetProcessWorkingSetSize(handle, NativeMethods.Constants.MinWorkingSet, NativeMethods.Constants.MaxWorkingSet);
        }
        catch
        {
            return false;
        }
        finally
        {
            if (handle != IntPtr.Zero) NativeMethods.CloseHandle(handle);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(int access, bool inherit, int processId);

    /// <summary>
    /// Memory in the standby list, in bytes, from the system performance
    /// counter.
    /// </summary>
    /// <remarks>
    /// This is the number that actually responds to EmptyStandbyList. Reporting
    /// "available" memory instead would make a flush look far more effective than
    /// it is.
    /// </remarks>
    public long GetStandbyMemoryBytes()
    {
        try
        {
            using var searcher = new System.Diagnostics.PerformanceCounter(
                "Memory", "Standby Cache Bytes", readOnly: true);
            return (long)searcher.NextValue();
        }
        catch
        {
            // The counter is missing on some SKUs and in most containers.
            return -1;
        }
    }

    /// <summary>Physical memory still in use by processes, in bytes.</summary>
    public long GetInUseMemoryBytes()
    {
        try
        {
            using var searcher = new System.Diagnostics.PerformanceCounter(
                "Memory", "In Use Bytes", readOnly: true);
            return (long)searcher.NextValue();
        }
        catch
        {
            return -1;
        }
    }

    public (double usedPercent, long totalBytes, long availableBytes, double totalGb, double freeGb) GetRamAndStorageStats()
    {
        var (ramPct, totalBytes, availBytes) = GetRamStats();
        var (storagePct, usedGb, totalGb) = GetStorageStats();
        var freeGb = totalGb - usedGb;
        return (ramPct, totalBytes, availBytes, totalGb, freeGb);
    }

    // ── STORAGE ──────────────────────────────────────────────────────────
    public (double usedPercent, double usedGb, double totalGb) GetStorageStats()
    {
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.System)) ?? "C:\\");
            if (!drive.IsReady) return (0, 0, 0);
            var total = drive.TotalSize;
            var free  = drive.AvailableFreeSpace;
            var usedGb = (total - free) / 1_073_741_824.0;
            var totalGb = total / 1_073_741_824.0;
            var pct = total > 0 ? (double)(total - free) / total * 100 : 0;
            return (Math.Round(pct, 1), Math.Round(usedGb, 2), Math.Round(totalGb, 2));
        }
        catch (Exception ex) { Log($"Storage query failed: {ex.Message}", "warning"); return (0, 0, 0); }
    }

    /// <summary>
    /// Read-only scan. Returns what a cleanup would remove without touching
    /// anything, so the UI can show a preview before the user commits.
    /// </summary>
    public string[] CleanupRoots { get; set; } = SafetyMode.Enabled ? Array.Empty<string>() : TempRoots().ToArray();

    public List<CleanupCandidate> ScanTempForCleanup()
    {
        var results = new List<CleanupCandidate>();
        CleanupCandidate Capture(string path)
        {
            PathSafety.RejectReparsePoints(path);
            if (Directory.Exists(path))
            {
                var files = Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Take(10001).Select(Capture).ToList();
                if (files.Count > 10000) throw new IOException("Preview bound exceeded; choose a smaller scope.");
                return new CleanupCandidate { Path = path, IsDirectory = true, Bytes = files.Sum(f => f.Bytes), Snapshot = files, Category = "Temporary files (snapshot)" };
            }
            var f = new FileInfo(path);
            return new CleanupCandidate { Path = f.FullName, Bytes = f.Length, LastWriteUtc = f.LastWriteTimeUtc, CreatedUtc = f.CreationTimeUtc, Category = "Temporary file" };
        }
        foreach (var root in CleanupRoots)
        {
            try
            {
                PathSafety.RejectReparsePoints(root);
                foreach (var child in Directory.GetFileSystemEntries(root))
                    try { results.Add(Capture(child)); } catch (Exception ex) { Log($"Preview skipped {child}: {ex.Message}", "warning"); }
            }
            catch (Exception ex) { Log($"Preview skipped {root}: {ex.Message}", "warning"); }
        }
        return results.OrderByDescending(c => c.Bytes).ToList();
    }

    public int LastCleanupErrors { get; private set; }

    public long CleanTempAndCache(IEnumerable<CleanupCandidate>? candidates = null)
    {
        LastCleanupErrors = 0;
        long reclaimedBytes = 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates ?? Array.Empty<CleanupCandidate>())
        {
            var files = candidate.IsDirectory ? candidate.Snapshot ?? new List<CleanupCandidate>() : new List<CleanupCandidate> { candidate };
            if (candidate.IsDirectory && candidate.Snapshot == null) { LastCleanupErrors++; Log($"Missing confirmed snapshot: {candidate.Path}", "warning"); }
            foreach (var file in files)
            {
                try
                {
                    var path = Path.GetFullPath(file.Path);
                    if (!seen.Add(path)) continue;
                    if (!CleanupRoots.Any(root => PathSafety.IsWithin(path, root))) throw new IOException("Outside approved temporary roots.");
                    PathSafety.RejectReparsePoints(path);
                    var info = new FileInfo(path);
                    if (!info.Exists) continue;
                    if (file.LastWriteUtc == null || info.Length != file.Bytes || info.LastWriteTimeUtc != file.LastWriteUtc || info.CreationTimeUtc != file.CreatedUtc) throw new IOException("File changed since preview; scan again.");
                    File.Delete(path);
                    reclaimedBytes += file.Bytes;
                }
                catch (Exception ex) { LastCleanupErrors++; Log($"Cleanup skipped {file.Path}: {ex.Message}", "warning"); }
            }
        }
        Log($"Removed {reclaimedBytes} bytes; {LastCleanupErrors} error(s). Empty directories are retained.", LastCleanupErrors == 0 ? "success" : "warning");
        return reclaimedBytes;
    }

    private static IEnumerable<string> TempRoots()
    {
        var roots = new List<string>();
        void Add(string? p)
        {
            if (!string.IsNullOrWhiteSpace(p) && Directory.Exists(p)
                && !roots.Contains(p, StringComparer.OrdinalIgnoreCase))
                roots.Add(p);
        }
        Add(Path.GetTempPath());
        Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp"));
        Add(Path.Combine(Environment.GetEnvironmentVariable("PROGRAMDATA") ?? "", "Temp"));
        return roots;
    }

    private static long DirectorySize(string path)
    {
        try
        {
            var di = new DirectoryInfo(path);
            if (!di.Exists) return 0;
            return di.EnumerateFiles("*", SearchOption.AllDirectories)
                       .Where(f => { try { return f.Length > 0; } catch { return false; } })
                       .Sum(f => { try { return f.Length; } catch { return 0L; } });
        }
        catch { return 0; }
    }

    // ── DNS ──────────────────────────────────────────────────────────────
    public string CurrentDns { get; private set; } = "System default";

    public string? SelectedDnsAdapterId { get; set; }
    public bool SetDns(string dnsIp, string dnsName)
    {
        if (string.IsNullOrWhiteSpace(SelectedDnsAdapterId)) { Log("Select a specific adapter before changing IPv4 DNS.", "warning"); return false; }
        var service = new DnsSettingsService(this);
        var ok = service.Change(SelectedDnsAdapterId, dnsIp);
        Log(service.LastMessage, ok ? "success" : "warning");
        if (ok) CurrentDns = dnsName;
        return ok;
    }

    public string? ReadActiveDnsIp()
    {
        try
        {
            var interfaces = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up && n.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback).ToList();
            var adapter = SelectedDnsAdapterId != null ? interfaces.FirstOrDefault(n => n.Id == SelectedDnsAdapterId) : interfaces.Count == 1 ? interfaces[0] : null;
            return adapter?.GetIPProperties().DnsAddresses.FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)?.ToString();
        }
        catch (Exception ex) { Log("DNS read failed: " + ex.Message, "warning"); return null; }
    }

    public (string name, string ip, bool matched) MatchCurrentDns(IEnumerable<(string name, string ip)> known)
    {
        var ip = ReadActiveDnsIp();
        if (string.IsNullOrWhiteSpace(ip)) return ("System default", "", false);
        foreach (var k in known)
        {
            if (string.Equals(k.ip, ip, StringComparison.OrdinalIgnoreCase))
                return (k.name, ip, true);
        }
        return ($"Custom ({ip})", ip, false);
    }

    private static (int code, string stdout, string stderr) RunProcess(string file, string args, int timeoutMs = 8000)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = file,
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        System.Diagnostics.Process? proc;
        try
        {
            proc = System.Diagnostics.Process.Start(psi);
        }
        catch (Exception ex)
        {
            // Process.Start throws rather than returning null when the binary
            // is missing or not permitted. Callers check for code == -1, so
            // translate it instead of letting it escape into the UI.
            return (-1, "", ex.Message);
        }
        if (proc == null) return (-1, "", "process did not start");

        // Drain both pipes on background threads BEFORE waiting. Reading
        // synchronously first deadlocks as soon as a child fills the 4KB
        // stdout buffer, which netsh and PowerShell both do readily. The old
        // order (ReadToEnd, ReadToEnd, WaitForExit) also applied the timeout
        // only after the reads had already blocked indefinitely.
        var stdoutTask = Task.Run(() => proc.StandardOutput.ReadToEnd());
        var stderrTask = Task.Run(() => proc.StandardError.ReadToEnd());

        if (!proc.WaitForExit(timeoutMs))
        {
            try { proc.Kill(entireProcessTree: true); } catch { }
            return (-1, "", $"timed out after {timeoutMs}ms");
        }

        // WaitForExit() with no argument also waits for the async readers to
        // finish, so no output is lost in the gap after the process exits.
        Task.WaitAll([stdoutTask, stderrTask], 5000);
        return (proc.ExitCode, stdoutTask.Result, stderrTask.Result);
    }



    // ── TRACKER / PRIVACY ────────────────────────────────────────────────
    public int TrackersBlocked { get; set; }

    private static (string Path, string Name) PrivacyKey(string setting) => setting switch
    {
        "Advertising ID" => (@"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled"),
        "Tailored Experiences" => (@"Software\Microsoft\Windows\CurrentVersion\Privacy", "TailoredExperiencesWithDiagnosticDataEnabled"),
        _ => throw new ArgumentException("Unsupported privacy setting.")
    };

    private static int? ReadPrivacyValue(string setting)
    {
        var (path, name) = PrivacyKey(setting);
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(path);
        var value = key?.GetValue(name);
        if (value == null) return null;
        if (value is not int n || key!.GetValueKind(name) != Microsoft.Win32.RegistryValueKind.DWord) throw new IOException("Unexpected registry value type; no change made.");
        return n;
    }

    private static void WritePrivacyValue(string setting, int? value)
    {
        SafetyMode.RequireMutationAllowed();
        var (path, name) = PrivacyKey(setting);
        using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(path);
        if (value == null) key.DeleteValue(name, false);
        else key.SetValue(name, value.Value, Microsoft.Win32.RegistryValueKind.DWord);
    }

    public bool? ReadPrivacyState(string setting)
    {
        try { return _readPrivacy(setting) switch { 0 => true, 1 => false, _ => null }; }
        catch (Exception ex) { Log($"{setting} state unknown: {ex.Message}", "warning"); return null; }
    }
    public bool ReadAdvertisingIdBlocked() => ReadPrivacyState("Advertising ID") == true;
    public bool ReadTailoredExperiencesOff() => ReadPrivacyState("Tailored Experiences") == true;

    public bool ApplyPrivacySetting(string trackerType, bool enable)
    {
        try
        {
            _ = PrivacyKey(trackerType);
            var before = _readPrivacy(trackerType);
            var desired = enable ? 0 : 1;
            if (before == desired) return true;
            // Save exact prior DWORD or absence before changing anything.
            var tx = new OptimizationTransaction { Title = $"Privacy: {trackerType}", Description = "Previous value captured; readback required.", Kind = UndoKind.PrivacyRegistry, UndoPayload = $"v3|{trackerType}|{(before.HasValue ? before.Value.ToString() : "absent")}|{desired}", Time = DateTime.Now };
            PushUndoTransaction(tx);
            _writePrivacy(trackerType, desired);
            if (_readPrivacy(trackerType) != desired) throw new IOException("Registry readback did not match requested state.");
            Log($"Updated {trackerType}; verified by readback.", "success");
            return true;
        }
        catch (Exception ex) { Log($"Privacy change failed for {trackerType}: {ex.Message}", "warning"); return false; }
    }

    public void ToggleTrackerBlock(string trackerType, bool enable)
    {
        if (enable)
        {
            TrackersBlocked++;
            Log($"Blocked {trackerType}.", "success");
        }
        else
        {
            TrackersBlocked = Math.Max(0, TrackersBlocked - 1);
            Log($"Turned off {trackerType} block.", "warning");
        }
    }

    public List<string> BuildHealthFindings(double ramPercent, double storagePercent, double freeStorageGb)
    {
        var findings = new List<string>();
        if (ramPercent >= 85) findings.Add($"RAM is high at {ramPercent:F0}%. Run maintenance to trim the working set and clear temp files.");
        else findings.Add($"RAM is fine at {ramPercent:F0}% used.");
        if (storagePercent >= 90 || freeStorageGb < 20)
            findings.Add($"Disk is tight: {freeStorageGb:F0} GB free ({storagePercent:F0}% used). Clear temp files.");
        else findings.Add($"Disk has room: {freeStorageGb:F0} GB free.");
        findings.Add(ReadPrivacyState("Advertising ID") switch
        {
            true => "Advertising ID is already blocked.",
            false => "Advertising ID is still on. Turn it off in Privacy Shield.",
            null => "Advertising ID state is unknown (unreadable, absent or unsupported value)."
        });
        if (string.IsNullOrWhiteSpace(ReadActiveDnsIp()))
            findings.Add("DNS is using the network default. Pick Cloudflare, Google, or Quad9 if you want a named resolver.");
        else
            findings.Add($"DNS server in use: {ReadActiveDnsIp()}.");
        return findings;
    }

    /// <summary>Test seam for the process runner.</summary>
    public (int code, string stdout, string stderr) RunProcessPublic(string file, string args, int timeoutMs = 8000)
        => RunProcess(file, args, timeoutMs);

    // ── CPU / PROCESSES / BOOT / DISK HEALTH ────────────────────────────
    public double GetCpuPercent()
    {
        try
        {
            // Two samples one second apart: CPU load is a rate, not a
            // cumulative counter, so a single read is meaningless.
            using var mos = new ManagementObjectSearcher("SELECT PercentProcessorTime FROM Win32_PerfFormattedData_PerfOS_Processor WHERE Name='_Total'");
            var first = ReadPerfCounter(mos);
            if (first < 0) return -1;
            Thread.Sleep(1000);
            var second = ReadPerfCounter(mos);
            if (second < 0) return first;
            // _Total across N processors is already normalised by WMI, so the
            // average of the two samples is the load percentage.
            return Math.Round((first + second) / 2.0, 1);
        }
        catch (Exception ex) { Log($"CPU query failed: {ex.Message}", "warning"); return -1; }
    }

    private static double ReadPerfCounter(ManagementObjectSearcher mos)
    {
        foreach (var mo in mos.Get())
            using (mo)
                if (mo["PercentProcessorTime"] != null)
                    return Convert.ToDouble(mo["PercentProcessorTime"]);
        return -1;
    }

    public IReadOnlyList<ProcessInfo> GetTopMemoryProcesses(int count = 5)
    {
        try
        {
            return System.Diagnostics.Process.GetProcesses()
                .Where(p => { try { return p.WorkingSet64 > 0; } catch { return false; } })
                .OrderByDescending(p => { try { return p.WorkingSet64; } catch { return 0L; } })
                .Take(count)
                .Select(p => new ProcessInfo
                {
                    Name = SafeName(p),
                    Pid = p.Id,
                    WorkingSetMb = Math.Round(p.WorkingSet64 / 1_048_576.0, 1)
                })
                .ToList();
        }
        catch (Exception ex) { Log($"Process query failed: {ex.Message}", "warning"); return Array.Empty<ProcessInfo>(); }
    }

    private static string SafeName(System.Diagnostics.Process p)
    {
        try { return p.ProcessName; } catch { return "?"; }
    }

    public TimeSpan GetLastBootTime()
    {
        try
        {
            using var mos = new ManagementObjectSearcher("SELECT LastBootUpTime FROM Win32_OperatingSystem");
            foreach (var mo in mos.Get())
                using (mo)
                    return DateTime.Now - ManagementDateTimeConverter.ToDateTime(mo["LastBootUpTime"]?.ToString() ?? "");
        }
        catch (Exception ex) { Log($"Boot time query failed: {ex.Message}", "warning"); }
        return TimeSpan.Zero;
    }

    public DiskHealth GetDiskHealth()
    {
        var result = new DiskHealth();
        try
        {
            // Physical disks only: Get-PhysicalDisk is unavailable on many
            // VMs, so a failure here is normal and not worth a warning.
            var (code, stdout, _) = RunProcess("powershell",
                "-NoProfile -Command \"Get-PhysicalDisk | Select-Object -First 1 FriendlyName, HealthStatus | ConvertTo-Csv -NoTypeInformation\"",
                15000);
            if (code != 0) return result;

            var lines = stdout.Split(new[] { (char)13, (char)10 }, StringSplitOptions.RemoveEmptyEntries)
                              .Skip(1).ToList();
            if (lines.Count == 0) return result;

            var parts = lines[0].Trim().Trim('"').Split(new[] { "\",\"" }, StringSplitOptions.None);
            result.Available = true;
            result.Model = parts.Length > 0 ? parts[0].Trim('"') : "Unknown";
            result.Status = parts.Length > 1 ? parts[1].Trim('"') : "Unknown";
        }
        catch { }
        return result;
    }

    // ── BLOATWARE ───────────────────────────────────────────────────────
// Only packages on this curated list are flagged. Previously every Store app
// was returned with IsBloat = true, which mislabelled Photos, Calculator and
// similar as "unused".
    private static readonly (string Match, string Reason)[] BloatwareBlocklist =
    {
        ("Microsoft.BingSearch",            "Web search — Edge or another browser already covers this"),
        ("Microsoft.BingWeather",           "Weather — not part of Windows"),
        ("Microsoft.BingFinance",          "Stock quotes — not part of Windows"),
        ("Microsoft.BingSports",           "Sports scores — not part of Windows"),
        ("Microsoft.3DBuilder",             "3D model editor — rarely used"),
        ("Microsoft.MixedReality.Portal",   "Mixed reality headset software"),
        ("Microsoft.Microsoft3DViewer",     "3D viewer — rarely used"),
        ("Microsoft.Office.OneNote",        "OneNote — only if you use the desktop version"),
        ("Microsoft.People",               "Legacy People app"),
        ("Microsoft.WindowsFeedbackHub",    "Feedback Hub — telemetry only"),
        ("Microsoft.YourPhone",             "Phone Link — only if you do not use it"),
        ("Microsoft.GetHelp",               "Get Help — only if you do not use it"),
        ("Microsoft.Getstarted",            "Windows tips / onboarding"),
        ("Microsoft.MicrosoftSolitaireCollection", "Solitaire — only if you do not play"),
        ("Microsoft.MicrosoftSudoku",       "Sudoku — only if you do not play"),
        ("Microsoft.XboxApp",               "Old Xbox app — superseded by the Store version"),
        ("Microsoft.XboxGameOverlay",       "Game overlay"),
        ("Microsoft.XboxGamingOverlay",     "Game overlay"),
        ("Microsoft.XboxIdentityProvider",  "Legacy Xbox sign-in"),
        ("Microsoft.XboxSpeechToText",      "Accessibility add-on"),
        ("Microsoft.WindowsMaps",            "Maps — not part of Windows"),
        ("Microsoft.WindowsFeedback",       "Feedback"),
        ("Microsoft.ZuneMusic",             "Legacy Groove Music"),
        ("Microsoft.ZuneVideo",             "Legacy Movies & TV"),
        ("Microsoft.Todos",                 "Legacy To Do"),
        ("Microsoft.PeopleList",            "Legacy contacts"),
        ("Microsoft.WindowsStore",          "Store — keep if you install apps"),
        ("Microsoft.Photos",                "Photos — keep if you use it"),
        ("Microsoft.WindowsCalculator",     "Calculator — keep if you use it"),
    };

    public List<BloatwareApp> GetUnusedApps()
    {
        var apps = new List<BloatwareApp>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var (code, stdout, err) = RunProcess(
                "powershell",
                "-NoProfile -Command \"Get-AppxPackage | Where-Object { $_.SignatureKind -eq 'Store' -and -not $_.IsFramework } | Select-Object Name, PackageFullName | ConvertTo-Csv -NoTypeInformation\"",
                30000);
            if (code != 0)
                Log($"App scan failed: {err}".Trim(), "warning");

            foreach (var line in stdout.Split(new[] { (char)13, (char)10 }, StringSplitOptions.RemoveEmptyEntries).Skip(1))
            {
                var parts = line.Trim().Trim('"').Split(new[] { "\",\"" }, StringSplitOptions.None);
                if (parts.Length < 2) continue;
                var name = parts[0].Trim('"');
                var fullName = parts[1].Trim('"');
                if (string.IsNullOrWhiteSpace(name) || !seen.Add(name)) continue;

                var match = BloatwareBlocklist.FirstOrDefault(b =>
                    name.StartsWith(b.Match, StringComparison.OrdinalIgnoreCase));
                if (match.Match == null) continue; // not on the list -> not bloatware

                apps.Add(new BloatwareApp
                {
                    Id = name,
                    Name = HumanisePackageName(name),
                    Package = fullName,
                    Size = "Installed",
                    IsBloat = true,
                    Reason = match.Reason
                });
            }
        }
        catch (Exception ex) { Log($"App scan failed: {ex.Message}", "warning"); }

        Log($"Scan complete. {apps.Count} removable candidate(s) from a curated list.", "system");
        return apps.OrderBy(a => a.Name).ToList();
    }

    private static string HumanisePackageName(string packageName)
    {
        var tail = packageName.Split('.').LastOrDefault() ?? packageName;
        var spaced = System.Text.RegularExpressions.Regex.Replace(tail, "(?<=[a-z0-9])([A-Z])", " $1");
        return spaced.Trim();
    }

    public bool UninstallApp(string packageId, string appName)
    {
        var result = new AppxService(this).RemoveStoreApp(packageId, appName);
        Log(result.Message, result.Success ? "success" : "warning");
        return result.Success;
    }

    // ── UNDO HISTORY (persisted to disk) ──────────────────────────────────
    private readonly List<OptimizationTransaction> _undoStack = new();

    public IReadOnlyList<OptimizationTransaction> GetUndoTransactions() => _undoStack.AsReadOnly();

    public void LoadUndoHistory()
    {
        lock (_undoStack)
        {
            _undoStack.Clear();
            _undoStack.AddRange(_history.Load());
        }
    }

    public void PushUndoTransaction(OptimizationTransaction tx)
    {
        lock (_undoStack)
        {
            // Ids must stay unique across restarts, so continue from the max
            // already on disk rather than UndoHistory.Count.
            tx.Id = _undoStack.Count == 0 ? 1 : _undoStack.Max(t => t.Id) + 1;
            var next = new[] { tx }.Concat(_undoStack).ToList();
            _history.Save(next);
            _undoStack.Insert(0, tx);
        }
    }

    public bool RemoveUndoTransaction(OptimizationTransaction tx)
    {
        lock (_undoStack)
        {
            var removed = _undoStack.Remove(tx);
            if (removed) _history.Save(_undoStack);
            return removed;
        }
    }

    // ── POWER & RESPONSIVENESS ────────────────────────────────────────────
    public Func<bool> IsPowerSafeMode { get; set; } = () => SafetyMode.Enabled;
    public Func<string>? ReadActivePowerGuid { get; set; }
    public Func<string, (bool Success, string Message)>? WriteActivePowerGuid { get; set; }

    public (bool Success, string Message) ApplyPowerPlan(string targetGuid, string targetName, PowerService? powerService = null)
    {
        if (!PowerService.IsKnownPlan(targetGuid))
            return (false, "Unknown power plan.");

        try
        {
            if (IsPowerSafeMode()) throw new InvalidOperationException("Action blocked by WinCare safety mode.");
            var svc = powerService ?? new PowerService();
            var readGuid = ReadActivePowerGuid ?? svc.GetActivePlanGuid;
            var writeGuid = WriteActivePowerGuid ?? svc.SetPowerPlan;

            var normalizedTarget = Guid.Parse(targetGuid.Trim()).ToString("D");
            var beforeGuid = readGuid();
            if (string.IsNullOrWhiteSpace(beforeGuid))
                return (false, "Could not read current power plan before switching; no change made.");

            var normalizedBefore = Guid.Parse(beforeGuid.Trim()).ToString("D");
            if (string.Equals(normalizedBefore, normalizedTarget, StringComparison.OrdinalIgnoreCase))
                return (true, $"{targetName} is already the active power plan.");

            var (ok, msg) = writeGuid(normalizedTarget);
            if (!ok) return (false, msg);

            var afterGuid = readGuid();
            if (!string.Equals(afterGuid, normalizedTarget, StringComparison.OrdinalIgnoreCase))
                return (false, "Power plan readback did not match the requested plan.");

            PushUndoTransaction(new OptimizationTransaction
            {
                Title = $"Power Plan: {targetName}",
                Description = $"Switched power scheme to {targetName}; previous scheme captured for rollback.",
                Kind = UndoKind.PowerPlan,
                UndoPayload = $"v1|{normalizedBefore}|{normalizedTarget}",
                Time = DateTime.Now
            });

            Log($"Power plan switched to {targetName} (verified).", "success");
            return (true, $"Active power plan set to {targetName}. Undo available in History & Logs.");
        }
        catch (Exception ex)
        {
            Log($"Power plan change failed: {ex.Message}", "warning");
            return (false, $"Power plan change blocked or failed: {ex.Message}");
        }
    }

    public StartupManagerService? StartupUndoService { get; set; }

    /// <summary>
    /// Performs a real reversal where one exists. Returns false when the
    /// transaction is display-only (e.g. deleted temp files), in which case
    /// the caller should not claim anything was restored.
    /// </summary>
    public bool TryRevert(OptimizationTransaction tx, out string message)
    {
        switch (tx.Kind)
        {
            case UndoKind.PrivacyRegistry when !string.IsNullOrEmpty(tx.UndoPayload):
                var parts = tx.UndoPayload.Split('|');
                if (parts.Length == 4 && parts[0] == "v3")
                {
                    try
                    {
                        _ = PrivacyKey(parts[1]);
                        int? previous = parts[2] == "absent" ? null : int.Parse(parts[2]);
                        if (_readPrivacy(parts[1]) != int.Parse(parts[3])) { message = "Setting changed externally; undo refused to avoid overwriting it."; return false; }
                        _writePrivacy(parts[1], previous);
                        var restored = _readPrivacy(parts[1]) == previous;
                        message = restored ? $"Restored previous {parts[1]} value (verified)." : "Restore readback failed.";
                        return restored;
                    }
                    catch (Exception ex) { message = ex.Message; return false; }
                }
                message = "Legacy or malformed recovery data cannot safely restore previous state.";
                return false;

            case UndoKind.DnsSettings when !string.IsNullOrEmpty(tx.UndoPayload):
                var dns = new DnsSettingsService(this);
                var dnsRestored = dns.Restore(tx.UndoPayload);
                message = dns.LastMessage;
                return dnsRestored;

            case UndoKind.PowerPlan when !string.IsNullOrEmpty(tx.UndoPayload):
                var pParts = tx.UndoPayload.Split('|');
                if (pParts.Length == 3 && pParts[0] == "v1" && PowerService.IsKnownPlan(pParts[1]) && PowerService.IsKnownPlan(pParts[2]))
                {
                    try
                    {
                        if (IsPowerSafeMode()) throw new InvalidOperationException("Action blocked by WinCare safety mode.");
                        var pSvc = new PowerService();
                        var readGuid = ReadActivePowerGuid ?? pSvc.GetActivePlanGuid;
                        var writeGuid = WriteActivePowerGuid ?? pSvc.SetPowerPlan;
                        var current = readGuid();
                        if (!string.Equals(current, pParts[2], StringComparison.OrdinalIgnoreCase))
                        {
                            message = "Active power plan changed externally; undo refused to avoid overwriting it.";
                            return false;
                        }
                        var (ok, err) = writeGuid(pParts[1]);
                        if (!ok) { message = err; return false; }
                        var verified = string.Equals(readGuid(), pParts[1], StringComparison.OrdinalIgnoreCase);
                        message = verified ? "Restored previous power plan (verified)." : "Power plan restore readback failed.";
                        return verified;
                    }
                    catch (Exception ex) { message = ex.Message; return false; }
                }
                message = "Malformed power-plan recovery payload.";
                return false;

            case UndoKind.StartupEntry when !string.IsNullOrEmpty(tx.UndoPayload):
                var sParts = tx.UndoPayload.Split('|');
                if (sParts.Length == 5 && sParts[0] == "v1" && bool.TryParse(sParts[3], out var previousEnabled))
                {
                    try
                    {
                        var location = Uri.UnescapeDataString(sParts[1]);
                        var entryName = Uri.UnescapeDataString(sParts[2]);
                        var expectedCmd = Uri.UnescapeDataString(sParts[4]);
                        var startupSvc = StartupUndoService ?? new StartupManagerService();
                        var match = startupSvc.GetStartupEntries().FirstOrDefault(e =>
                            string.Equals(e.Location, location, StringComparison.Ordinal) &&
                            string.Equals(e.Name, entryName, StringComparison.OrdinalIgnoreCase));
                        if (match == null || match.IsEnabled == previousEnabled || !string.Equals(match.Command, expectedCmd, StringComparison.Ordinal))
                        {
                            message = "Startup entry changed externally or no longer matches; undo refused.";
                            return false;
                        }
                        var toggled = startupSvc.ToggleStartupEntry(match, previousEnabled);
                        message = toggled
                            ? $"Restored startup entry '{entryName}' to {(previousEnabled ? "Enabled" : "Disabled")}."
                            : $"Could not restore startup entry '{entryName}'.";
                        return toggled;
                    }
                    catch (Exception ex) { message = ex.Message; return false; }
                }
                message = "Malformed startup recovery payload.";
                return false;

            case UndoKind.ReinstallApp:
                message = "App reinstallation is not guaranteed. Use the original installer or Store.";
                return false;

            default:
                message = "Nothing to restore — deleted files cannot be recovered.";
                return false;
        }
    }
}

internal static partial class NativeMethods
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    public static extern IntPtr GetCurrentProcess();

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    public static extern bool SetProcessWorkingSetSize(IntPtr hProcess, IntPtr dwMinimumWorkingSetSize, IntPtr dwMaximumWorkingSetSize);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    public static extern bool CloseHandle(IntPtr hObject);

    [System.Runtime.InteropServices.DllImport("psapi.dll", SetLastError = true)]
    private static extern bool EmptyWorkingSet(IntPtr hProcess);



    internal static class Constants
    {
        public static readonly IntPtr MaxWorkingSet = unchecked((IntPtr)0xFFFFFFFF);
        public static readonly IntPtr MinWorkingSet = new IntPtr(0x100000);
    }
}
