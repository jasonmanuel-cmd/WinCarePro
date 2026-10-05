using WinCareDesktop.Services;
using Xunit;

namespace WinCareDesktop.Tests;

/// <summary>
/// Covers the power-plan service against this machine's real configuration.
/// Read-only: nothing here changes the active plan.
/// </summary>
public class PowerServiceTests
{
    [Fact]
    public void Reads_the_active_power_plan()
    {
        var guid = new PowerService().GetActivePlanGuid();

        // An empty read means the P/Invoke failed, which would make every plan
        // look inactive and silently disable the whole feature.
        Assert.False(string.IsNullOrWhiteSpace(guid),
            "PowerGetActiveScheme returned nothing, so no plan can ever be marked active.");
    }

    [Fact]
    public void Always_reports_exactly_one_active_plan()
    {
        var plans = new PowerService().GetPowerPlans();

        Assert.Equal(3, plans.Count);
        Assert.Single(plans, p => p.Active);

        // The active plan must be one Windows recognises, otherwise "Use it"
        // would be offered for a scheme that cannot be applied.
        var known = new[]
        {
            "381b4222-f694-41f0-9685-ff5bb260df2e",
            "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c",
            "a1841308-3541-4fab-bc81-f71556f20b4a",
        };
        Assert.Contains(plans.First(p => p.Active).Guid.ToLowerInvariant(), known);
    }

    [Fact]
    public void Plan_guids_use_the_bare_format_that_the_constants_use()
    {
        var plans = new PowerService().GetPowerPlans();

        // Guid.ToString("B") wraps in braces, which silently broke every
        // comparison against the constants. Guard the format directly.
        foreach (var plan in plans)
        {
            Assert.DoesNotContain('{', plan.Guid);
            Assert.DoesNotContain('}', plan.Guid);
            Assert.True(Guid.TryParse(plan.Guid, out _), $"'{plan.Guid}' is not a parseable GUID.");
        }
    }

    [Fact]
    public void The_active_guid_matches_the_listed_plans_exactly()
    {
        var svc = new PowerService();
        var active = svc.GetActivePlanGuid();

        Assert.Contains(svc.GetPowerPlans(), p =>
            string.Equals(p.Guid, active, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-guid")]
    [InlineData("11111111-2222-3333-4444-555555555555")]
    public void Rejects_a_plan_it_does_not_recognise(string guid)
    {
        Assert.False(PowerService.IsKnownPlan(guid));
    }

    [Theory]
    // Bare lower-case, which is what GetActivePlanGuid returns.
    [InlineData("381b4222-f694-41f0-9685-ff5bb260df2e")]
    // Upper-case.
    [InlineData("381B4222-F694-41F0-9685-FF5BB260DF2E")]
    // Brace-wrapped, which is what Guid.ToString("B") produces.
    [InlineData("{381B4222-F694-41F0-9685-FF5BB260DF2E}")]
    // Surrounding whitespace.
    [InlineData("  381b4222-f694-41f0-9685-ff5bb260df2e  ")]
    public void Recognises_a_stock_plan_regardless_of_format(string guid)
    {
        Assert.True(PowerService.IsKnownPlan(guid));
    }

    [Fact]
    public void Every_offered_plan_is_recognisable_by_the_switch()
    {
        // If a listed plan failed IsKnownPlan, the UI would offer "Use it" for a
        // scheme that SetPowerPlan then refuses as unknown.
        foreach (var plan in new PowerService().GetPowerPlans())
            Assert.True(PowerService.IsKnownPlan(plan.Guid),
                $"{plan.Name} is offered but would be rejected by SetPowerPlan.");
    }

    [Fact]
    public void Rejects_a_plan_before_touching_the_system()
    {
        var (ok, message) = new PowerService().SetPowerPlan("not-a-guid");

        Assert.False(ok);
        Assert.False(string.IsNullOrWhiteSpace(message));
    }
}
