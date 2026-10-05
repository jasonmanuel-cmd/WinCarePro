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
    public SystemOptimizerService(HistoryStore? history = null)
    {
        _history = history ?? new HistoryStore();
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
            var mc = new ManagementObjectSearcher("SELECT * FROM Win32_OperatingSystem");
            foreach (var mo in mc.Get())
            {
                var total = Convert.ToInt64(mo["TotalVisibleMemorySize"]) * 1024;
                var free  = Convert.ToInt64(mo["FreePhysicalMemory"]) * 1024;
                var usedPct = total > 0 ? (double)(total - free) / total * 100 : 0;
                return (Math.Round(usedPct, 1), total, free);
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
        var (code, _, err) = RunProcess("ipconfig", "/flushdns");
        Log(code == 0 ? "DNS cache flushed." : $"DNS cache flush failed: {err}".Trim(), code == 0 ? "success" : "warning");
    }

    public static bool IsElevated()
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        var principal = new System.Security.Principal.WindowsPrincipal(identity);
        return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }

    public void FlushStandbyMemory()
    {
        // Try the real thing first. EmptyStandbyList genuinely drops the
        // standby list; the old code only ever trimmed *this* process's
        // working set and then logged "Standby list flushed", which was false.
        if (NativeMethods.TryEmptyStandbyList())
        {
            Log("Standby memory list flushed (EmptyStandbyList).", "success");
            return;
        }

        try
        {
            var hProcess = NativeMethods.GetCurrentProcess();
            NativeMethods.SetProcessWorkingSetSize(hProcess, NativeMethods.Constants.MinWorkingSet, NativeMethods.Constants.MaxWorkingSet);
            NativeMethods.CloseHandle(hProcess);
            Log("Trimmed this process's working set. Full standby flush needs Administrator (EmptyStandbyList was unavailable).", "warning");
        }
        catch (Exception ex)
        {
            Log($"Working set trim failed: {ex.Message}", "warning");
        }
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
    public List<CleanupCandidate> ScanTempForCleanup()
    {
        var results = new List<CleanupCandidate>();
        foreach (var root in TempRoots())
        {
            string[] children;
            try { children = Directory.GetFileSystemEntries(root); }
            catch { continue; }

            foreach (var child in children)
            {
                try
                {
                    var isDir = Directory.Exists(child);
                    var bytes = isDir ? DirectorySize(child) : new FileInfo(child).Length;
                    if (bytes <= 0) continue;
                    results.Add(new CleanupCandidate
                    {
                        Path = child,
                        Bytes = bytes,
                        Category = Path.GetFileName(root).Equals("Temp", StringComparison.OrdinalIgnoreCase)
                            ? "User temp"
                            : "System temp",
                        IsDirectory = isDir
                    });
                }
                catch { }
            }
        }
        return results.OrderByDescending(c => c.Bytes).ToList();
    }

    /// <summary>
    /// Deletes only the supplied candidates. Takes the list explicitly rather
    /// than re-scanning, so the user deletes exactly what they were shown.
    /// </summary>
    public long CleanTempAndCache(IEnumerable<CleanupCandidate>? candidates = null)
    {
        if (candidates == null)
        {
            Log("Scanning temp folders and system caches...", "system");
            candidates = ScanTempForCleanup();
        }

        long reclaimedBytes = 0;
        foreach (var c in candidates)
        {
            try
            {
                if (c.IsDirectory)
                {
                    if (!Directory.Exists(c.Path)) continue;
                    reclaimedBytes += DirectorySize(c.Path);
                    Directory.Delete(c.Path, true);
                }
                else
                {
                    if (!File.Exists(c.Path)) continue;
                    var len = new FileInfo(c.Path).Length;
                    File.Delete(c.Path);
                    reclaimedBytes += len;
                }
            }
            catch { /* locked / in use — expected for anything running */ }
        }

        var reclaimedGb = Math.Round(reclaimedBytes / 1_073_741_824.0, 2);
        Log($"Reclaimed {reclaimedGb} GB from {candidates.Count()} temp/cache item(s).", "success");
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

    public bool SetDns(string dnsIp, string dnsName)
    {
        Log($"Attempting DNS switch to {dnsName} ({dnsIp})...", "system");
        var iface = GetActiveNetworkInterface();
        if (string.IsNullOrWhiteSpace(iface))
        {
            Log("No active network adapter found.", "warning");
            return false;
        }
        var (code, _, err) = RunProcess("netsh", $"interface ip set dns name=\"{iface}\" source=static addr={dnsIp}");
        if (code == 0)
        {
            CurrentDns = dnsName;
            Log($"DNS set to {dnsName} ({dnsIp}) on {iface}.", "success");
            return true;
        }
        Log($"DNS change failed (exit {code}). Administrator rights are required. {err}".Trim(), "warning");
        return false;
    }

    public string? ReadActiveDnsIp()
    {
        try
        {
            var mos = new ManagementObjectSearcher("SELECT DNSServerSearchOrder FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled=true");
            foreach (var mo in mos.Get())
            {
                if (mo["DNSServerSearchOrder"] is string[] servers && servers.Length > 0)
                    return servers[0];
            }
        }
        catch (Exception ex) { Log($"DNS read failed: {ex.Message}", "warning"); }
        return null;
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

    private string GetActiveNetworkInterface()
    {
        try
        {
            var mos = new ManagementObjectSearcher("SELECT NetConnectionID, IPAddress FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled=true");
            foreach (var mo in mos.Get())
            {
                if (mo["IPAddress"] is string[] ips && ips.Length > 0)
                {
                    var id = mo["NetConnectionID"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(id)) return id;
                }
            }
        }
        catch { /* fall through */ }
        return "";
    }

    // ── TRACKER / PRIVACY ────────────────────────────────────────────────
    public int TrackersBlocked { get; set; }

    public bool ReadAdvertisingIdBlocked()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo");
            return key?.GetValue("Enabled") is int v && v == 0;
        }
        catch { return false; }
    }

    public bool ReadTailoredExperiencesOff()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Privacy");
            return key?.GetValue("TailoredExperiencesWithDiagnosticDataEnabled") is int v && v == 0;
        }
        catch { return false; }
    }

    public bool ApplyPrivacySetting(string trackerType, bool enable)
    {
        try
        {
            if (trackerType is "Advertising ID" or "Background Tracking")
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo");
                key?.SetValue("Enabled", enable ? 0 : 1, Microsoft.Win32.RegistryValueKind.DWord);
            }
            else
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Privacy");
                key?.SetValue("TailoredExperiencesWithDiagnosticDataEnabled", enable ? 0 : 1, Microsoft.Win32.RegistryValueKind.DWord);
            }
            ToggleTrackerBlock(trackerType, enable);
            return true;
        }
        catch (Exception ex)
        {
            Log($"Privacy change failed for {trackerType}: {ex.Message}", "warning");
            return false;
        }
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
        findings.Add(ReadAdvertisingIdBlocked()
            ? "Advertising ID is already blocked."
            : "Advertising ID is still on. Turn it off in Privacy Shield.");
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
                    WorkingSetMb = Math.Round(p.WorkingSet64 / 1_073_741_824.0, 2)
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

            var lines = stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
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

            foreach (var line in stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Skip(1))
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
        Log($"Attempting to uninstall: {appName} ({packageId})", "system");
        if (string.IsNullOrWhiteSpace(packageId)) return false;
        var safe = packageId.Replace("'", "");
        var (code, _, err) = RunProcess("powershell", $"-NoProfile -Command \"Get-AppxPackage -Name '{safe}' | Remove-AppxPackage\"", 20000);
        if (code == 0 && string.IsNullOrWhiteSpace(err))
        {
            Log($"Uninstalled {appName}.", "success");
            return true;
        }
        Log($"Uninstall failed for {appName}: {(string.IsNullOrWhiteSpace(err) ? "exit " + code : err.Trim())}", "warning");
        return false;
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
            _undoStack.Insert(0, tx);
            _history.Save(_undoStack);
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
                if (parts.Length == 2)
                {
                    var ok = ApplyPrivacySetting(parts[0], parts[1] == "1");
                    message = ok
                        ? $"Restored {parts[0]} to its previous value."
                        : $"Could not restore {parts[0]}. Registry write failed.";
                    return ok;
                }
                message = "Undo payload was malformed.";
                return false;

            case UndoKind.ReinstallApp when !string.IsNullOrEmpty(tx.UndoPayload):
                var (code, _, err) = RunProcess("powershell",
                    $"-NoProfile -Command \"Get-AppxPackage -Name '{tx.UndoPayload.Replace("'", "")}' | ForEach-Object {{ Add-AppxPackage -Register ('$($_.InstallLocation)\\AppXManifest.xml') -DisableDevelopmentMode }}\"",
                    30000);
                if (code == 0)
                {
                    message = $"Reinstalled {tx.UndoPayload}.";
                    return true;
                }
                message = $"Reinstall failed for {tx.UndoPayload}. {err}".Trim();
                return false;

            default:
                message = "Nothing to restore — deleted files cannot be recovered.";
                return false;
        }
    }
}

internal static partial class NativeMethods
{
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    public static extern IntPtr GetCurrentProcess();

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    public static extern bool SetProcessWorkingSetSize(IntPtr hProcess, IntPtr dwMinimumWorkingSetSize, IntPtr dwMaximumWorkingSetSize);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    public static extern bool CloseHandle(IntPtr hObject);

    [System.Runtime.InteropServices.DllImport("psapi.dll", SetLastError = true)]
    private static extern bool EmptyWorkingSet(IntPtr hProcess);

    private static bool s_standbySupported;
    private static bool s_standbyProbed;

    /// <summary>
    /// Attempts the real standby-list flush via psapi!EmptyWorkingSet on the
    /// memory compression process. Probes availability once and caches it so
    /// we do not pay the exception cost on every call.
    /// </summary>
    public static bool TryEmptyStandbyList()
    {
        if (s_standbyProbed) return s_standbySupported;
        s_standbyProbed = true;
        try
        {
            // MemoryCompression is the process that actually owns the standby
            // list on modern Windows. Opening it requires elevation, so a
            // failure here just means "run as administrator".
            var proc = System.Diagnostics.Process.GetProcessesByName("Memory Compression")
                           .FirstOrDefault()
                        ?? System.Diagnostics.Process.GetProcessesByName("MemoryCompression").FirstOrDefault();
            if (proc == null) return false;
            using (proc)
            {
                var h = proc.Handle; // forces OpenProcess
                s_standbySupported = EmptyWorkingSet(h);
            }
        }
        catch { s_standbySupported = false; }
        return s_standbySupported;
    }

    internal static class Constants
    {
        public static readonly IntPtr MaxWorkingSet = unchecked((IntPtr)0xFFFFFFFF);
        public static readonly IntPtr MinWorkingSet = new IntPtr(0x100000);
    }
}
