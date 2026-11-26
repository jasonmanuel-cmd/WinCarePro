using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;

[assembly: InternalsVisibleTo("WinCareDesktop.Tests")]
namespace WinCareDesktop.Services;

/// <summary>Fixed Windows PowerShell modules only. No user/module-path fallback.</summary>
internal static class RescuePowerShellPolicy
{
    internal static ProcessStartInfo CreateStartInfo(string script)
    {
        var windows=Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if(string.IsNullOrWhiteSpace(windows))throw new IOException("Windows system directory unavailable.");
        var system=Path.Combine(windows,"System32");
        var home=Path.Combine(system,"WindowsPowerShell","v1.0");
        var modules=Path.Combine(home,"Modules");
        var executable=Path.Combine(home,"powershell.exe");
        PathSafety.RejectReparsePoints(executable);
        var info=new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=system};
        // Never inherit PSModulePath, user profiles, startup hooks or arbitrary PATH entries.
        info.Environment.Clear();
        info.Environment["SystemRoot"]=windows;info.Environment["WINDIR"]=windows;
        info.Environment["PATH"]=system+";"+home;
        info.Environment["TEMP"]=Path.GetTempPath();info.Environment["TMP"]=Path.GetTempPath();
        info.Environment["PSModulePath"]=modules;
        info.Environment["PSModuleAnalysisCachePath"]="NUL";
        info.Environment["POWERSHELL_TELEMETRY_OPTOUT"]="1";
        var bootstrap=new StringBuilder("$ErrorActionPreference='Stop'; $PSModuleAutoLoadingPreference='None'; try { ");
        // PowerShell reconstructs PSModulePath at startup; constrain it again inside the child.
        bootstrap.Append("$env:PSModulePath='").Append(Quote(modules)).Append("'; ");
        var required=new (string Module,string[] Commands)[] {
            ("Microsoft.PowerShell.Utility",new[]{"Select-Object","ConvertTo-Json"}),
            ("Microsoft.PowerShell.Management",Array.Empty<string>()),
            ("Microsoft.PowerShell.Security",new[]{"Get-AuthenticodeSignature"}),
            ("Microsoft.PowerShell.Diagnostics",new[]{"Get-WinEvent"}),
            ("CimCmdlets",Array.Empty<string>()),
            ("Defender",new[]{"Start-MpScan","Get-MpThreatDetection"})
        };
        foreach(var (module,commands) in required)
        {
            var directory=Path.Combine(modules,module);
            var manifest=Path.Combine(directory,module+".psd1");
            if(!File.Exists(manifest))throw new IOException("Required system provider module unavailable.");
            PathSafety.RejectReparsePoints(directory);
            bootstrap.Append("Microsoft.PowerShell.Core\\Import-Module -Name '").Append(Quote(manifest)).Append("' -ErrorAction Stop; ");
            foreach(var command in commands)
            {
                // Inbox snap-in-backed modules report PSHOME as ModuleBase. Pin the exact
                // manifest/CDXML path rather than assuming ModuleBase equals its directory.
                var commandPath=module=="Defender"?Path.Combine(directory,command=="Start-MpScan"?"MSFT_MpScan.cdxml":"MSFT_MpThreatDetection.cdxml"):manifest;
                bootstrap.Append("$c=Microsoft.PowerShell.Core\\Get-Command -Name '").Append(module).Append('\\').Append(command)
                    .Append("' -ErrorAction Stop; if($null -eq $c.Module -or $c.Module.Path -ine '").Append(Quote(commandPath))
                    .Append("'){throw 'Unexpected provider command origin'}; ");
            }
        }
        bootstrap.Append(script).Append(" } catch { [Console]::Error.WriteLine('Provider request failed; permission, provenance or policy may deny access.'); exit 1 }");
        foreach(var arg in new[]{"-NoLogo","-NoProfile","-NonInteractive","-Command",bootstrap.ToString()})info.ArgumentList.Add(arg);
        return info;
    }
    private static string Quote(string value)=>value.Replace("'","''");
}
