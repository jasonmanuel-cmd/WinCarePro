namespace WinCareDesktop.Services;
public sealed record DefenderReading(string Mode,bool? Antivirus,bool? Realtime,DateTimeOffset? SignatureUpdated,string SignatureVersion)
{
    public string ProtectionText => Mode switch
    {
        "Passive" => "Passive — Defender is not the primary antivirus. Review your active provider.",
        "EDR Block Mode" => "EDR block mode — not normal active antivirus protection.",
        "Normal" when Antivirus == true && Realtime == true => "Active — real-time protection reported on; not a malware clearance.",
        "Normal" when Antivirus == false || Realtime == false => "Disabled or reduced protection — review Windows Security.",
        _ => "Unknown — Defender mode or protection fields are unavailable."
    };
    public string SignatureText => SignatureUpdated is { } time && time <= DateTimeOffset.UtcNow
        ? $"Signatures: {SignatureVersion} • updated {time.ToLocalTime():g} • age {(DateTimeOffset.UtcNow-time).TotalDays:F1} days. Not a scan result."
        : "Unknown signature update time. Review Windows Security; this is not a scan result.";
}
public static class DefenderStatusReader
{
    public static Task<DefenderReading> ReadAsync(CancellationToken token) => Task.Run(()=>
    {
        token.ThrowIfCancellationRequested();
        using var search=new System.Management.ManagementObjectSearcher(@"root\Microsoft\Windows\Defender", "SELECT * FROM MSFT_MpComputerStatus");
        search.Options.Timeout=TimeSpan.FromSeconds(8);
        using var result=search.Get();
        foreach(System.Management.ManagementObject item in result)
        {
            using(item)
            {
                token.ThrowIfCancellationRequested();
                object? Get(string name)=>item.Properties.Cast<System.Management.PropertyData>().FirstOrDefault(p=>p.Name==name)?.Value;
                DateTimeOffset? updated=null;
                if(Get("AntivirusSignatureLastUpdated") is string stamp)
                    try {updated=new DateTimeOffset(System.Management.ManagementDateTimeConverter.ToDateTime(stamp));} catch(ArgumentException) { }
                return new DefenderReading(Get("AMRunningMode") as string ?? "",Get("AntivirusEnabled") as bool?,Get("RealTimeProtectionEnabled") as bool?,updated,Get("AntivirusSignatureVersion") as string ?? "Unknown");
            }
        }
        throw new InvalidOperationException("Defender status unavailable.");
    },token);
}
public static class RescueActions
{
    // Coarse safety permission only; page-specific availability and identity gates still apply.
    public static bool CanStartScan => !SafetyMode.Enabled;
    public static bool CanCloseProcess => !SafetyMode.Enabled;
    public static void OpenWindowsSecurity(Action? launch=null)
    {
        SafetyMode.RequireMutationAllowed();
        if (launch != null) launch();
        else using (System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:windowsdefender") { UseShellExecute=true })) { }
    }
}
