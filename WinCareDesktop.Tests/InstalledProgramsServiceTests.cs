using System;
using System.Linq;
using WinCareDesktop.Services;
using Xunit;
using Xunit.Abstractions;

namespace WinCareDesktop.Tests;

/// <summary>
/// Proves the uninstaller reads this machine's real programs and builds safe
/// commands for them. Read-only: nothing here removes anything.
/// </summary>
public class InstalledProgramsServiceTests
{
    private readonly ITestOutputHelper _out;

    public InstalledProgramsServiceTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void Finds_real_installed_programs_on_this_machine()
    {
        var programs = new InstalledProgramsService().GetInstalledPrograms();

        Assert.NotEmpty(programs);

        // This PC has far more than a handful of registered programs; a small
        // count would mean the registry enumeration silently returned nothing.
        Assert.True(programs.Count > 20,
            $"Only {programs.Count} programs found, which suggests enumeration is broken.");

        _out.WriteLine($"found {programs.Count} programs");
        foreach (var p in programs.Take(8))
            _out.WriteLine($"  {p.DisplaySize,-12} {p.Name}  ({p.Publisher})");
    }

    [Fact]
    public void Every_listed_program_has_something_to_run()
    {
        var programs = new InstalledProgramsService().GetInstalledPrograms();

        foreach (var p in programs)
        {
            Assert.True(p.UninstallString.Length > 0 || p.QuietUninstallString.Length > 0,
                $"{p.Name} was listed but has no uninstall string.");
            Assert.False(string.IsNullOrWhiteSpace(p.Name));
        }
    }

    [Fact]
    public void Duplicate_names_are_collapsed_across_32_and_64_bit_views()
    {
        var programs = new InstalledProgramsService().GetInstalledPrograms();

        var dupes = programs
            .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.Empty(dupes);
    }

    [Fact]
    public void Known_sizes_are_sorted_largest_first()
    {
        var programs = new InstalledProgramsService().GetInstalledPrograms()
            .Where(p => p.SizeKnown)
            .ToList();

        for (var i = 1; i < programs.Count; i++)
        {
            Assert.True(programs[i - 1].SizeBytes >= programs[i].SizeBytes,
                $"{programs[i - 1].Name} ({programs[i - 1].SizeBytes}) should sort " +
                $"before {programs[i].Name} ({programs[i].SizeBytes}).");
        }
    }

    [Fact]
    public void Builds_a_command_for_a_program_that_has_an_uninstaller()
    {
        var programs = new InstalledProgramsService().GetInstalledPrograms();
        var target = programs.First();

        var ok = InstalledProgramsService.TryBuildUninstallCommand(
            target, out var file, out var args, out _);

        Assert.True(ok, $"Could not build an uninstall command for {target.Name}");
        Assert.False(string.IsNullOrWhiteSpace(file));
        Assert.NotNull(args);
    }

    [Fact]
    public void Refuses_to_build_a_command_when_there_is_no_uninstaller()
    {
        var orphan = new Models.InstalledProgram { Name = "Fake" };

        var ok = InstalledProgramsService.TryBuildUninstallCommand(
            orphan, out _, out _, out var warning);

        Assert.False(ok);
        Assert.Contains("uninstaller", warning, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    // Quoted path with a space in it.
    [InlineData("\"C:\\Program Files\\App\\unins000.exe\" /VERYSILENT", "C:\\Program Files\\App\\unins000.exe", "/VERYSILENT")]
    // Unquoted path, arguments after the first space.
    [InlineData("C:\\Temp\\uninstall.exe /S", "C:\\Temp\\uninstall.exe", "/S")]
    // No arguments at all.
    [InlineData("C:\\Temp\\uninstall.exe", "C:\\Temp\\uninstall.exe", "")]
    public void Splits_command_strings_correctly(string command, string file, string args)
    {
        Assert.True(InstalledProgramsService.TrySplit(command, out var f, out var a));
        Assert.Equal(file, f);
        Assert.Equal(args, a);
    }

    [Fact]
    public void Warns_when_a_program_has_no_silent_uninstaller()
    {
        // Only an interactive uninstall string, so the vendor UI will appear.
        var program = new Models.InstalledProgram
        {
            Name = "Interactive",
            UninstallString = "\"C:\\Program Files\\X\\unins.exe\""
        };

        InstalledProgramsService.TryBuildUninstallCommand(program, out _, out _, out var warning);

        Assert.NotNull(warning);
        Assert.Contains("no silent mode", warning, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Prefers_the_silent_uninstaller_when_one_exists()
    {
        var program = new Models.InstalledProgram
        {
            Name = "Both",
            UninstallString = "\"C:\\X\\unins.exe\"",
            QuietUninstallString = "\"C:\\X\\unins.exe\" /SILENT"
        };

        InstalledProgramsService.TryBuildUninstallCommand(program, out _, out var args, out var warning);

        Assert.Contains("/SILENT", args);
        Assert.Null(warning);
    }

    [Fact]
    public void FormatBytes_reads_as_human_sizes()
    {
        Assert.Equal("0 B", Models.InstalledProgram.FormatBytes(0));
        Assert.Equal("1 KB", Models.InstalledProgram.FormatBytes(1024));
        Assert.Equal("1 MB", Models.InstalledProgram.FormatBytes(1024L * 1024));
        Assert.Equal("1.5 GB", Models.InstalledProgram.FormatBytes(1536L * 1024 * 1024));
        Assert.Equal("2 GB", Models.InstalledProgram.FormatBytes(2L * 1024 * 1024 * 1024));
    }
}