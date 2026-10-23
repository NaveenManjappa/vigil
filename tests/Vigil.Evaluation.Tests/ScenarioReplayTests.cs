using Vigil.Domain.Detection;
using Vigil.Simulator.Scenarios;

namespace Vigil.Evaluation.Tests;

public sealed class ScenarioReplayTests
{
    private static readonly RuleSet Rules = RuleSet.Load(Path.Combine(AppContext.BaseDirectory, "config", "rules.v1.json"));
    private static readonly Scenario Margaret = Scenario.Load(Path.Combine(AppContext.BaseDirectory, "scenarios", "margaret-ato.json"));
    private static readonly Scenario Patel = Scenario.Load(Path.Combine(AppContext.BaseDirectory, "scenarios", "patel-house-move.json"));

    [Fact]
    public void Margaret_reaches_High_at_the_email_change_and_Critical_at_the_bank_change()
    {
        var rows = ScenarioReplay.Replay(Margaret, Rules);

        Assert.Equal(
            ["R0 10 Low", "R1 40 Medium", "R2 60 High", "R3 90 Critical"],
            rows.Select(r => $"{string.Join("+", r.NewHits.Select(h => h.RuleId))} {r.Score} {r.Band}"));
    }

    [Fact]
    public void Mr_Patel_stays_Low_with_no_hits_at_every_step()
    {
        var rows = ScenarioReplay.Replay(Patel, Rules);

        Assert.All(rows, r => Assert.Equal((0, 0, RiskBand.Low), (r.NewHits.Count, r.Score, r.Band)));
    }

    [Fact]
    public void A_different_rule_set_changes_the_verdict_without_touching_the_scenario()
    {
        var stricter = Rules with { Bands = Rules.Bands with { High = 70 } };

        var emailChange = ScenarioReplay.Replay(Margaret, stricter)[2];

        Assert.Equal((60, RiskBand.Medium), (emailChange.Score, emailChange.Band));
    }

    [Fact]
    public void The_rendered_replay_says_when_High_was_first_reached()
    {
        var text = ScenarioReplay.Render(Margaret, Rules, ScenarioReplay.Replay(Margaret, Rules));

        Assert.Contains("First reached High at step 3, EmailChanged (+10m)", text);
        Assert.Contains("13 baseline logins", text);
    }

    [Fact]
    public void The_rendered_replay_says_when_High_was_never_reached()
    {
        var text = ScenarioReplay.Render(Patel, Rules, ScenarioReplay.Replay(Patel, Rules));

        Assert.Contains("Never reached High; peak Low", text);
    }
}
