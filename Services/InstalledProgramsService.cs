using Microsoft.Win32;
using WinCareDesktop.Models;

namespace WinCareDesktop.Services;

/// <summary>
/// Enumerates installed programs from the Windows uninstall registry and
/// delegates removal to each vendor's own uninstaller.
/// </summary>
/// <remarks>
/// WinCare never deletes an application's folder. Removing a program's
/// directory by hand leaves its registry keys, services, scheduled tasks and
/// file associations behind, and breaks uninstall, repair and upgrade. Handing
/// the job to the registered uninstall string is slower but is the only way to
/// remove a program cleanly.
/// </remarks>
public class InstalledProgramsService
{
    private static readonly string[] Roots =
    {
        @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
        @"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
    };

    /// <summary>
    /// Every program that exposes an uninstall string, largest first.
    /// </summary>
    /// <remarks>
    /// Must run off the UI thread: touching dozens of registry hives takes long
    /// enough to be felt as a stall.
    /// </remarks>
    public List<InstalledProgram> GetInstalledPrograms(bool includeSystemComponents = false)
    {
        var results = new List<InstalledProgram>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in Roots)
        {
            var hive = root.StartsWith(@"HKEY_CURRENT_USER", StringComparison.Ordinal)
                ? RegistryHive.CurrentUser
                : RegistryHive.LocalMachine;

            using var hiveKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
            using var baseKey = hiveKey.OpenSubKey(SubPath(root));
            if (baseKey == null) continue;

            foreach (var name in baseKey.GetSubKeyNames())
            {
                using var key = baseKey.OpenSubKey(name);
                if (key == null) continue;

                var program = ReadProgram(key, name, root);
                if (program == null) continue;

                // The 32-bit and 64-bit views frequently list the same program.
                // Keying on display name keeps one row per app.
                if (!seen.Add(program.Name)) continue;

                if (!includeSystemComponents && program.IsSystemComponent) continue;
                results.Add(program);
            }
        }

        // Known sizes first, biggest first, then the rest alphabetically so the
        // list does not reshuffle arbitrarily between refreshes.
        return results
            .OrderByDescending(p => p.SizeKnown)
            .ThenByDescending(p => p.SizeBytes)
            .ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>Registry path with the hive root stripped, which OpenSubKey needs.</summary>
    private static string SubPath(string root) => root[(root.IndexOf('\\') + 1)..];

    private static InstalledProgram? ReadProgram(RegistryKey key, string subKey, string root)
    {
        var name = (key.GetValue("DisplayName") as string)?.Trim();
        if (string.IsNullOrWhiteSpace(name)) return null;

        // An uninstall string is what makes a program removable at all.
        var uninstall = (key.GetValue("UninstallString") as string)?.Trim() ?? "";
        var quiet = (key.GetValue("QuietUninstallString") as string)?.Trim() ?? "";
        if (uninstall.Length == 0 && quiet.Length == 0) return null;

        // Guard against the placeholder rows Windows keeps for patch packages.
        if (name.StartsWith("KB", StringComparison.OrdinalIgnoreCase) && name.Contains("Update for"))
            return null;

        var size = ReadSize(key);

        return new InstalledProgram
        {
            KeyPath = $@"{root}\{subKey}",
            Name = name,
            Publisher = (key.GetValue("Publisher") as string)?.Trim() ?? "",
            Version = (key.GetValue("DisplayVersion") as string)?.Trim() ?? "",
            InstallDate = FormatInstallDate(key.GetValue("InstallDate")),
            SizeBytes = size,
            SizeKnown = size > 0,
            UninstallString = uninstall,
            QuietUninstallString = quiet,
            InstallLocation = (key.GetValue("InstallLocation") as string)?.Trim() ?? "",
            IsSystemComponent = ReadFlag(key, "SystemComponent"),
        };
    }

    private static long ReadSize(RegistryKey key)
    {
        // EstimatedSize is in KB, and is the only size most installers record.
        var kb = key.GetValue("EstimatedSize");
        if (kb is int i && i > 0) return (long)i * 1024;
        return 0;
    }

    private static bool ReadFlag(RegistryKey key, string name)
    {
        var v = key.GetValue(name);
        return v switch
        {
            int i => i != 0,
            string s => s is "1" or "true" or "True",
            _ => false,
        };
    }

    private static string FormatInstallDate(object? raw)
    {
        // Two shapes exist: a DateTime, and the legacy 8-digit "YYYYMMDD" string.
        if (raw is DateTime dt) return dt.ToString("d MMM yyyy");
        if (raw is string s && s.Length == 8 && s.All(char.IsDigit))
        {
            if (DateTime.TryParseExact(s, "yyyyMMdd", null,
                    System.Globalization.DateTimeStyles.None, out var parsed))
                return parsed.ToString("d MMM yyyy");
        }
        return "";
    }

    /// <summary>
    /// Builds the argument list for a program's uninstaller, preferring the
    /// silent string when the vendor supplies one.
    /// </summary>
    /// <remarks>
    /// Returns false for anything we cannot drive safely. Falling back to
    /// deleting the install folder is never an option here.
    /// </remarks>
    public static bool TryBuildUninstallCommand(InstalledProgram program,
        out string file, out string arguments, out string? warning)
    {
        file = "";
        arguments = "";
        warning = null;

        var command = !string.IsNullOrWhiteSpace(program.QuietUninstallString)
            ? program.QuietUninstallString
            : program.UninstallString;

        if (string.IsNullOrWhiteSpace(command))
        {
            warning = "This program does not publish an uninstaller.";
            return false;
        }

        // MSI packages have a canonical command line; prefer it when we can
        // recognise one, because the recorded string is often MsiExec /I...
        if (command.Contains("MsiExec.exe", StringComparison.OrdinalIgnoreCase))
        {
            if (TryExtractMsiProductCode(program, out var productCode))
            {
                file = "msiexec.exe";
                arguments = $"/x {productCode} /qn /norestart";
                return true;
            }
        }

        if (!TrySplit(command, out file, out arguments))
        {
            warning = "Could not parse this program's uninstall command.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(program.QuietUninstallString))
        {
            // No silent string means we must show the vendor's own UI so the
            // user sees exactly what is about to happen.
            warning = "This uninstaller has no silent mode, so its own window will open. "
                     + "Read it before confirming.";
        }

        return true;
    }

    private static bool TryExtractMsiProductCode(InstalledProgram program, out string code)
    {
        code = "";
        using var hiveKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = hiveKey.OpenSubKey(SubPath(program.KeyPath));
        var guid = key?.GetValue("ProductCode") as string;
        if (string.IsNullOrWhiteSpace(guid)) return false;
        code = guid;
        return true;
    }

    /// <summary>
    /// Splits a command string into an executable and its arguments, honouring
    /// quoted paths.
    /// </summary>
    public static bool TrySplit(string command, out string file, out string arguments)
    {
        file = "";
        arguments = "";
        command = command.Trim();

        if (command.StartsWith('"'))
        {
            var close = command.IndexOf('"', 1);
            if (close < 0) return false;
            file = command[1..close];
            arguments = command[(close + 1)..].Trim();
            return file.Length > 0;
        }

        var split = command.IndexOf(' ');
        if (split < 0)
        {
            file = command;
            return file.Length > 0;
        }

        file = command[..split];
        arguments = command[(split + 1)..].Trim();
        return file.Length > 0;
    }
}