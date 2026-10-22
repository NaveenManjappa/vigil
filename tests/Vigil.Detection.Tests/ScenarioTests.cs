using Vigil.Domain.Detection;
using Vigil.Domain.Events;
using Vigil.Simulator.Scenarios;

namespace Vigil.Detection.Tests;

/// <summary>The PROJECT.md Section 3 stories, told by the Scenarios the simulator posts to the API.</summary>
public sealed class ScenarioTests
{
    private static readonly RuleSet Rules = RuleSet.Load(Path.Combine(AppContext.BaseDirectory, "config", "rules.v1.json"));

    [Fact]
    public void Margaret_is_High_at_the_email_change_before_any_bank_change()
    {
        var timeline = new MargaretAto().Events();
        var email = timeline.Single(e => e.Type == EventType.EmailChanged);
        var bank = timeline.Single(e => e.Type == EventType.BankDetailsChanged);
        Assert.True(email.OccurredAt < bank.OccurredAt);

        // Everything Vigil had seen when the email change arrived.
        var seen = timeline.Where(e => e.OccurredAt <= email.OccurredAt).ToList();
        var alert = Assert.Single(Detector.Detect(seen, Rules));

        Assert.Equal(["R0", "R1", "R2"], alert.Hits.Select(h => h.RuleId));
        Assert.Equal(60, alert.Score);
        Assert.Equal(RiskBand.High, alert.Band);
        Assert.Equal(email.EventId, alert.Hits[^1].EvidenceEventIds[^1]);
    }

    [Fact]
    public void Mr_Patel_moving_house_raises_no_alert()
    {
        Assert.Empty(Detector.Detect(new MrPatelHouseMove().Events(), Rules));
    }
}
