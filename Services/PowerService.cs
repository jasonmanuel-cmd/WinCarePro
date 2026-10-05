using System.Runtime.InteropServices;
using WinCareDesktop.Models;

namespace WinCareDesktop.Services;

/// <summary>
/// Power plan enumeration and switching, plus the display/sleep timers.
/// </summary>
/// <remarks>
/// Switching the active power plan is a real, reversible system change, so it
/// is recorded in the Undo History with the previous plan as the payload. The
/// GUI GUIDs are the well-known ones every Windows install carries; the active
/// plan is discovered rather than assumed.
/// </remarks>
public class PowerService
{
    private const string BalancedGuid = "381b4222-f694-41f0-9685-ff5bb260df2e";
    private const string HighPerformanceGuid = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
    private const string PowerSaverGuid = "a1841308-3541-4fab-bc81-f71556f20b4a";

    private static readonly Guid[] KnownGuids =
    {
        new(BalancedGuid),
        new(HighPerformanceGuid),
        new(PowerSaverGuid),
    };

    [DllImport("powrprof.dll", CharSet = CharSet.Unicode)]
    private static extern uint PowerSetActiveScheme(IntPtr userRootPowerKey, ref Guid schemeGuid);

    /// <remarks>
    /// This is the only active-plan entry point powrprof.dll actually exports;
    /// there is no PowerReadActiveScheme. The returned pointer must be released
    /// with LocalFree, not the marshaller's own free, because the buffer belongs
    /// to the system rather than to a managed allocation.
    /// </remarks>
    [DllImport("powrprof.dll", CharSet = CharSet.Unicode)]
    private static extern uint PowerGetActiveScheme(IntPtr userRootPowerKey,
        out IntPtr activePolicyGuid);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr handle);

    /// <summary>
    /// The three stock plans, with <see cref="PowerPlan.Active"/> set on whichever
    /// is currently applied.
    /// </summary>
    /// <remarks>
    /// Only stock plans are offered. A machine with custom or OEM plans still
    /// gets a working set, and guessing at third-party scheme GUIDs would risk
    /// switching to something the user did not ask for.
    /// </remarks>
    public List<PowerPlan> GetPowerPlans()
    {
        var active = GetActivePlanGuid();

        // Ordinal-ignore-case because the two sides can differ in case and
        // brace wrapping depending on which API produced them. Comparing
        // case-sensitively is what made every plan look inactive.
        bool IsActive(string guid) =>
            active.Length > 0 && string.Equals(active, guid, StringComparison.OrdinalIgnoreCase);

        return new List<PowerPlan>
        {
            new() { Guid = BalancedGuid,        Name = "Balanced",        Active = IsActive(BalancedGuid) },
            new() { Guid = HighPerformanceGuid, Name = "High performance", Active = IsActive(HighPerformanceGuid) },
            new() { Guid = PowerSaverGuid,      Name = "Power saver",     Active = IsActive(PowerSaverGuid) },
        };
    }

    /// <summary>
    /// The GUID of the active plan in bare "D" form, or empty when it cannot be
    /// read.
    /// </summary>
    /// <remarks>
    /// The bare form matters. Comparing raw strings against constants used to
    /// fail silently because <c>Guid.ToString("B")</c> wraps the value in braces
    /// and upper-cases it, so no plan ever matched and every plan looked
    /// inactive. Parsing into a real Guid and reformatting removes that class
    /// of bug entirely.
    /// </remarks>
    public string GetActivePlanGuid()
    {
        try
        {
            if (PowerGetActiveScheme(IntPtr.Zero, out var ptr) != 0) return "";

            try
            {
                var bytes = new byte[16];
                Marshal.Copy(ptr, bytes, 0, 16);
                return new Guid(bytes).ToString("D");
            }
            finally
            {
                if (ptr != IntPtr.Zero) LocalFree(ptr);
            }
        }
        catch
        {
            return "";
        }
    }

    /// <summary>
    /// True when <paramref name="guid"/> names one of the stock plans.
    /// </summary>
    /// <remarks>
    /// Split out from <see cref="SetPowerPlan"/> so recognition can be tested
    /// without switching the machine's power plan. Comparison is done on parsed
    /// Guids rather than strings, because the caller may supply any casing or the
    /// brace-wrapped "B" format.
    /// </remarks>
    public static bool IsKnownPlan(string guid) =>
        !string.IsNullOrWhiteSpace(guid)
        && Guid.TryParse(guid.Trim(), out var parsed)
        && KnownGuids.Contains(parsed);

    /// <summary>
    /// Applies a stock power plan.
    /// </summary>
    /// <returns>True on success, with a message suitable for showing to the user.</returns>
    public (bool Success, string Message) SetPowerPlan(string guid)
    {
        if (!IsKnownPlan(guid))
            return (false, "Unknown power plan.");

        try
        {
            var target = new Guid(guid);
            var result = PowerSetActiveScheme(IntPtr.Zero, ref target);

            return result == 0
                ? (true, "Power plan applied.")
                : (false, $"Windows rejected the change (code {result}). Try running as administrator.");
        }
        catch (Exception ex)
        {
            return (false, "Could not change the power plan: " + ex.Message);
        }
    }

    /// <summary>True when the machine is running on battery.</summary>
    public bool IsOnBattery()
    {
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher(
                "SELECT BatteryStatus FROM Win32_Battery");
            foreach (var item in searcher.Get())
            {
                // 1 means discharging; anything else means plugged in or no battery.
                return Convert.ToInt32(item["BatteryStatus"]) == 1;
            }
        }
        catch
        {
            // No battery, or WMI unavailable.
        }
        return false;
    }
}