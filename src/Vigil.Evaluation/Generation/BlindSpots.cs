using Vigil.Domain.Detection;
using Vigil.Simulator.Scenarios;

namespace Vigil.Evaluation;

/// <param name="RulesHit">Every Rule that did fire, so the report shows how close the journey came.</param>
public sealed record BlindSpot(Scenario Scenario, RiskBand Peak, IReadOnlyList<string> RulesHit);

/// <param name="DryRun">How many Generated Scenarios were dry-run.</param>
/// <param name="Fraud">How many of them are labelled fraud, the ones that can be Blind Spots.</param>
public sealed record BlindSpotReview(string RulesVersion, int DryRun, int Fraud, IReadOnlyList<BlindSpot> BlindSpots);

public static class BlindSpots
{
    /// <summary>
    /// Dry-runs every Generated Scenario and keeps the fraud ones that never reach High. Graph Rules are skipped, as in
    /// any dry run, so a journey that only a Graph Rule would catch still counts: its Account has no links here.
    /// </summary>
    public static BlindSpotReview Find(IReadOnlyList<Scenario> generated, RuleSet rules)
    {
        var fraud = generated.Where(s => s.Label?.IsFraud == true).ToList();
        var spots = fraud
            .Select(scenario => (scenario, rows: ScenarioReplay.Replay(scenario, rules)))
            .Where(run => run.rows.All(r => r.Band < RiskBand.High))
            .Select(run => new BlindSpot(
                run.scenario,
                run.rows.Select(r => r.Band).DefaultIfEmpty(RiskBand.Low).Max(),
                run.rows.SelectMany(r => r.NewHits).Select(h => h.RuleId).Distinct().ToList()))
            .ToList();
        return new BlindSpotReview(rules.Version, generated.Count, fraud.Count, spots);
    }
}
