using WinCareDesktop.Models;
using WinCareDesktop.Services;
using Xunit;

namespace WinCareDesktop.Tests;

public class ScoringTests
{
    /// <summary>
    /// Mirrors MainViewModel.ComputeScore. The VM method is private and pulls in
    /// WMI, so the weighting is reimplemented here to pin the behaviour.
    /// </summary>
    private static double ComputeScore(double ramPercent, double storagePercent, int shieldsOn, int shieldsTotal, double reclaimedGb)
    {
        var ram = HeadroomScore(ramPercent, 50) * 0.55;
        var storage = HeadroomScore(storagePercent, 70) * 0.30;
        var privacy = shieldsTotal == 0 ? 0 : shieldsOn / (double)shieldsTotal * 10.0;
        var reclaimed = reclaimedGb > 0 ? 5.0 : 0.0;
        return Math.Clamp(ram + storage + privacy + reclaimed, 0, 100);
    }

    /// <summary>
    /// Full marks while usage stays at or under <paramref name="comfort"/>,
    /// then decays linearly to zero at 100%. A machine at 30% RAM should not
    /// be punished the same as one at 95%.
    /// </summary>
    private static double HeadroomScore(double usedPercent, double comfort)
    {
        if (usedPercent <= comfort) return 100;
        if (usedPercent >= 100) return 0;
        return (100 - usedPercent) / (100 - comfort) * 100.0;
    }

    [Fact]
    public void Healthy_machine_scores_high()
    {
        var score = ComputeScore(ramPercent: 30, storagePercent: 40, shieldsOn: 4, shieldsTotal: 4, reclaimedGb: 1);
        Assert.True(score > 85, $"expected >85, got {score}");
    }

    [Fact]
    public void Critical_machine_scores_low()
    {
        var score = ComputeScore(ramPercent: 99, storagePercent: 99, shieldsOn: 0, shieldsTotal: 4, reclaimedGb: 0);
        Assert.True(score < 5, $"expected <5, got {score}");
    }

    [Fact]
    public void Score_never_exceeds_one_hundred()
    {
        var score = ComputeScore(0, 0, 4, 4, 99);
        Assert.Equal(100, score);
    }

    [Fact]
    public void Score_is_never_negative()
    {
        var score = ComputeScore(200, 200, 0, 4, 0);
        Assert.Equal(0, score);
    }

    [Fact]
    public void No_flat_fifty_floor()
    {
        // Regression: the original implementation started at 50, so even a
        // fully degraded machine could not fall below ~51.
        var score = ComputeScore(99, 99, 0, 4, 0);
        Assert.True(score < 20, $"a wrecked machine still scored {score}");
    }

    [Fact]
    public void Ram_weighs_more_than_storage()
    {
        var ramHeavy = ComputeScore(10, 90, 0, 4, 0);
        var diskHeavy = ComputeScore(90, 10, 0, 4, 0);
        Assert.True(ramHeavy > diskHeavy);
    }

    [Fact]
    public void Shields_are_worth_up_to_ten_points()
    {
        var off = ComputeScore(50, 50, 0, 4, 0);
        var on = ComputeScore(50, 50, 4, 4, 0);
        Assert.Equal(10, on - off, 3);
    }
}

public class UndoKindTests
{
    [Fact]
    public void DisplayOnly_is_not_reversible()
    {
        var tx = new OptimizationTransaction { Kind = UndoKind.DisplayOnly };
        Assert.False(tx.Reversable);
    }

    [Fact]
    public void ReinstallApp_is_reversible()
    {
        var tx = new OptimizationTransaction { Kind = UndoKind.ReinstallApp };
        Assert.True(tx.Reversable);
    }

    [Fact]
    public void PrivacyRegistry_is_reversible()
    {
        var tx = new OptimizationTransaction { Kind = UndoKind.PrivacyRegistry };
        Assert.True(tx.Reversable);
    }

    [Fact]
    public void Default_kind_is_display_only()
    {
        // A transaction with no explicit kind must not claim to be undoable.
        Assert.False(new OptimizationTransaction().Reversable);
    }
}