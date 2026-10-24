using Vigil.Domain.Detection;
using Vigil.Evaluation.Tuning;

namespace Vigil.Evaluation.Tests;

public sealed class RuleTuningTests
{
    private static readonly RuleSet Rules = RuleSet.Load(Path.Combine(AppContext.BaseDirectory, "config", "rules.v2.json"));

    private static DecidedCase Fraud(params string[] ruleIds) => new(Guid.NewGuid(), DecidedCase.ConfirmedFraud, "stand-in", ruleIds);

    private static DecidedCase Genuine(params string[] ruleIds) => new(Guid.NewGuid(), "FalsePositive", "stand-in", ruleIds);

    [Fact]
    public void A_Rules_precision_is_the_share_of_decided_Cases_containing_it_that_were_ConfirmedFraud()
    {
        DecidedCase[] decisions =
        [
            Fraud("R0", "R1", "R2"),
            Fraud("R1", "R3"),
            Genuine("R0", "R2"),
            Genuine("R2"),
        ];

        var precision = RuleTuning.Precision(decisions, Rules).ToDictionary(p => p.RuleId, p => p.Precision);

        Assert.Equal(new Rate(1, 2), precision["R0"]);
        Assert.Equal(new Rate(2, 2), precision["R1"]);
        Assert.Equal(new Rate(1, 3), precision["R2"]);
        Assert.Equal(new Rate(1, 1), precision["R3"]);
        // A Rule no decided Case contained is still listed, so a reader sees it had no evidence rather than no row.
        Assert.Equal(new Rate(0, 0), precision["G1"]);
    }

    [Fact]
    public void A_Rule_that_fired_twice_in_one_Case_counts_that_Case_once()
    {
        var precision = RuleTuning.Precision([Genuine("R0", "R0"), Fraud("R0")], Rules).Single(p => p.RuleId == "R0");

        Assert.Equal(new Rate(1, 2), precision.Precision);
    }

    private static DecisionPrecision Measured(string ruleId, int points, int fraud, int of) => new(ruleId, ruleId, points, new Rate(fraud, of));

    [Fact]
    public void A_Rule_wrong_more_often_than_not_with_enough_decisions_has_its_points_halved_to_the_nearest_5()
    {
        var suggestions = RuleTuning.Suggest(
        [
            Measured("R2", 20, 9, 29),  // 31%: 20 halves to 10.
            Measured("G2", 25, 2, 5),   // 40% at exactly the minimum support: 12.5 rounds to 15.
            Measured("R1", 30, 5, 10),  // Exactly 50% is not below it.
            Measured("R3", 30, 0, 4),   // Too few decisions to judge.
            Measured("R0", 10, 0, 0),   // Never reached a Case.
        ]);

        Assert.Equal(
        [
            new WeightSuggestion("R2", "R2", new Rate(9, 29), 20, 10),
            new WeightSuggestion("G2", "G2", new Rate(2, 5), 25, 15),
        ], suggestions);
    }

    [Fact]
    public void A_Rule_already_at_5_points_gets_no_suggestion_because_halving_cannot_lower_it()
    {
        Assert.Empty(RuleTuning.Suggest([Measured("R0", 5, 0, 8)]));
    }
}
