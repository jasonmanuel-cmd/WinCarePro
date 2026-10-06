using WinCareDesktop.Models;
using WinCareDesktop.Services;
using Xunit;

namespace WinCareDesktop.Tests;

/// <summary>
/// Covers Store-app enumeration and removal. Read-only against the machine:
/// nothing here calls RemoveStoreApp, because that would genuinely uninstall an
/// app.
/// </summary>
public class AppxServiceTests
{
    [Fact]
    public void Finds_real_store_apps_on_this_machine()
    {
        var apps = new AppxService().GetStoreApps();

        Assert.NotEmpty(apps);

        foreach (var app in apps)
        {
            Assert.Equal(UninstallKind.Appx, app.Kind);
            Assert.False(string.IsNullOrWhiteSpace(app.PackageFullName));
            Assert.False(string.IsNullOrWhiteSpace(app.Name));

            // Store apps have no classic uninstall string. Reporting one would
            // send the UI down the wrong removal path.
            Assert.True(string.IsNullOrEmpty(app.UninstallString),
                $"{app.Name} was listed as an Appx app but carries an uninstall string.");
        }
    }

    [Fact]
    public void Offers_well_formed_package_names()
    {
        var apps = new AppxService().GetStoreApps();

        Assert.NotEmpty(apps);

        foreach (var app in apps)
        {
            // Vendor_Package_Version_arch__publisher
            var segments = app.PackageFullName.Split('_');
            Assert.True(segments.Length >= 3,
                app.PackageFullName + " does not look like a full package name.");

            Assert.Contains('.', app.PackageFullName);
        }

        // The framework filter must not be so aggressive that the list is
        // effectively empty. A machine with over 100 packages still has
        // dozens of genuinely removable apps.
        Assert.True(apps.Count >= 10,
            $"only {apps.Count} Store apps were offered, which suggests the "
            + "framework filter is too aggressive.");
    }

    [Fact]
    public void Reports_no_size_rather_than_a_made_up_one()
    {
        var apps = new AppxService().GetStoreApps();

        foreach (var app in apps)
        {
            Assert.False(app.SizeKnown);
            Assert.Equal(0, app.SizeBytes);
        }
    }

    [Fact]
    public void Names_are_readable_rather_than_package_identifiers()
    {
        var apps = new AppxService().GetStoreApps();

        foreach (var app in apps)
        {
            Assert.DoesNotContain('.', app.Name);
        }
    }

    [Fact]
    public void Refuses_an_empty_package_name()
    {
        var (ok, message) = new AppxService().RemoveStoreApp("", "Anything");

        Assert.False(ok);
        Assert.Contains("package name", message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    // Only the last dot-separated segment survives, so the vendor prefix does
    // not leak into the UI.
    [InlineData("Microsoft.BingNews", "BingNews")]
    [InlineData("Microsoft.XboxSpeechToTextOverlay", "XboxSpeechToTextOverlay")]
    // Some packages have no dot at all.
    [InlineData("MSTeams", "MSTeams")]
    // A trailing separator would otherwise yield an empty name, so the original
    // is kept rather than rendering a blank row.
    [InlineData("Vendor.", "Vendor.")]
    public void Humanises_package_names(string input, string expected)
    {
        Assert.Equal(expected, AppxService.Humanise(input));
    }

    [Theory]
    [InlineData("CN=Microsoft Corporation, O=Microsoft Inc., C=US", "Microsoft Corporation")]
    [InlineData("CN=Contoso", "Contoso")]
    [InlineData("Plain Publisher", "Plain Publisher")]
    [InlineData("", "")]
    public void Strips_the_manifest_publisher_wrapper(string input, string expected)
    {
        Assert.Equal(expected, AppxService.CleanPublisher(input));
    }

    [Fact]
    public void Parses_a_json_array_of_apps()
    {
        const string json = """
            [{"Name":"Microsoft.WindowsCalculator","FullName":"Microsoft.WindowsCalculator_11.0_x64__8wekyb3d8bbwe","Publisher":"CN=Microsoft Corporation, O=Microsoft Inc.","Version":"11.2307.0.0","InstallPath":"C:\\Program Files\\Calc"}]
            """;

        var records = AppxService.ParseJson(json);

        var record = Assert.Single(records);
        Assert.Equal("Microsoft.WindowsCalculator", record.Name);
        Assert.Equal("11.2307.0.0", record.Version);
        Assert.Contains("Microsoft Corporation", record.Publisher);
    }

    [Fact]
    public void Parses_a_single_object_without_array_wrapping()
    {
        // ConvertTo-Json emits a bare object when the pipeline returns one item,
        // so a parser that only handles arrays silently returns nothing.
        const string json = """
            {"Name":"Only.App","FullName":"Only.App_1.0_x64__abc","Publisher":null,"Version":"1.0"}
            """;

        var record = Assert.Single(AppxService.ParseJson(json));

        Assert.Equal("Only.App", record.Name);
        Assert.Equal("Only.App_1.0_x64__abc", record.FullName);
        Assert.Equal("", record.Publisher);
    }

    [Fact]
    public void Parses_multiple_objects_with_nulls_and_escapes()
    {
        const string json = """
            [{"Name":"A.B","FullName":"A.B_1_x__z","Publisher":null,"Version":"1"},
             {"Name":"C.D","FullName":"C.D_2_x__z","Publisher":"CN=E \"Quoted\" Ltd","Version":"2"}]
            """;

        var records = AppxService.ParseJson(json);

        Assert.Equal(2, records.Count);
        Assert.Equal("", records[0].Publisher);
        Assert.Contains("Quoted", records[1].Publisher);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("[")]
    [InlineData("[]")]
    public void Survives_empty_or_malformed_input(string json)
    {
        // Must return an empty list, never throw: this runs on the UI's async
        // path and a malformed payload should not take the panel down.
        Assert.Empty(AppxService.ParseJson(json));
    }
}
