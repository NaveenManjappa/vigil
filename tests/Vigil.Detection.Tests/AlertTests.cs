using Vigil.Domain.Detection;

namespace Vigil.Detection.Tests;

public sealed class AlertTests
{
    private static readonly DateTimeOffset Monday = new(2026, 9, 21, 9, 0, 0, TimeSpan.Zero);
    private static readonly BandThresholds Bands = new(Medium: 30, High: 60, Critical: 90);

    private static RuleHit Hit(string ruleId, int points, DateTimeOffset at) => new(ruleId, points, [Guid.NewGuid()], at, $"{ruleId} hit");

    [Fact]
    public void The_Score_is_the_sum_of_the_hits_and_sets_the_band()
    {
        var alert = new Alert("ACC-1", "v3");

        alert.Add([Hit("R0", 10, Monday), Hit("G2", 25, Monday)], Bands);
        Assert.Equal((35, RiskBand.Medium), (alert.Score, alert.Band));

        alert.Add([Hit("R1", 30, Monday.AddMinutes(4))], Bands);
        Assert.Equal((65, RiskBand.High), (alert.Score, alert.Band));
    }

    [Fact]
    public void A_Rule_counts_once_however_often_it_hits()
    {
        var alert = new Alert("ACC-1", "v3");
        alert.Add([Hit("R0", 10, Monday)], Bands);

        var added = alert.Add([Hit("R0", 10, Monday.AddMinutes(4)), Hit("R1", 30, Monday.AddMinutes(4))], Bands);

        Assert.Equal(["R1"], added.Select(h => h.RuleId));
        Assert.Equal(["R0", "R1"], alert.Hits.Select(h => h.RuleId));
        Assert.Equal(40, alert.Score);
    }

    [Fact]
    public void An_Alert_goes_quiet_once_its_last_hit_is_older_than_the_risk_period()
    {
        var alert = new Alert("ACC-1", "v3");
        alert.Add([Hit("R3", 30, Monday)], Bands);

        Assert.False(alert.HasGoneQuiet(Monday.AddDays(7), riskPeriodDays: 7));
        Assert.True(alert.HasGoneQuiet(Monday.AddDays(7).AddMinutes(1), riskPeriodDays: 7));
    }
}
