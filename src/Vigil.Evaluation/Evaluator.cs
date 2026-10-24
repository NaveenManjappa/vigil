using Vigil.Detection;
using Vigil.Domain.Datasets;
using Vigil.Domain.Detection;
using Vigil.Domain.Events;

namespace Vigil.Evaluation;

/// <summary>A count out of a total, kept as both so a report can show "17 / 30" and not just a percentage.</summary>
public sealed record Rate(int Count, int Of)
{
    public double? Value => Of == 0 ? null : (double)Count / Of;
}

public sealed record RulePrecision(string RuleId, string Name, Rate Precision);

/// <param name="PeakBands">How many Accounts' highest Alert was in each band; an Account with no Alert counts as Low.</param>
public sealed record PersonaOutcome(Persona Persona, bool IsFraud, int Accounts, int Flagged, IReadOnlyDictionary<RiskBand, int> PeakBands);

public sealed record EvaluationResult(
    string RulesVersion,
    Rate DetectionRate,
    Rate FalsePositiveRate,
    Rate EarlyDetectionRate,
    IReadOnlyList<RulePrecision> RulePrecision,
    IReadOnlyList<PersonaOutcome> Personas);

/// <summary>
/// Scores a labelled Dataset with the same <see cref="Detector"/> the API uses, in memory, one Account at a time.
/// An Account is flagged when any of its Alerts reached High or above.
/// </summary>
public static class Evaluator
{
    /// <param name="graphHits">Each Account's candidate graph Rule Hits from <see cref="GraphReplay"/>; none when the graph is off.</param>
    public static EvaluationResult Evaluate(
        IEnumerable<LabelledAccount> dataset, RuleSet ruleSet, IReadOnlyDictionary<string, IReadOnlyList<RuleHit>>? graphHits = null)
    {
        var scored = Score(dataset, ruleSet, graphHits);
        var fraud = scored.Where(s => s.Account.Label.IsFraud).ToList();
        var genuine = scored.Where(s => !s.Account.Label.IsFraud).ToList();
        var ato = fraud.Where(s => s.Account.Label.FraudKind == FraudKind.Ato).ToList();

        var hits = scored.SelectMany(s => s.Alerts.SelectMany(a => a.Hits).Select(h => (h.RuleId, s.Account.Label.IsFraud))).ToList();
        var precision = ruleSet.Rules
            .Select(r => new RulePrecision(r.Id, r.Name, new Rate(
                hits.Count(h => h.RuleId == r.Id && h.IsFraud),
                hits.Count(h => h.RuleId == r.Id))))
            .ToList();

        // Shows where the rates come from, including the misses PROJECT.md Section 10.1 expects (slow-burn, ring).
        var personas = scored
            .GroupBy(s => s.Account.Label.Persona)
            .OrderBy(g => g.Key)
            .Select(g => new PersonaOutcome(
                g.Key,
                g.First().Account.Label.IsFraud,
                g.Count(),
                g.Count(s => FlaggedAt(s.Alerts, ruleSet) is not null),
                Enum.GetValues<RiskBand>().ToDictionary(band => band, band => g.Count(s => s.PeakBand == band))))
            .ToList();

        return new EvaluationResult(
            ruleSet.Version,
            new Rate(fraud.Count(s => FlaggedAt(s.Alerts, ruleSet) is not null), fraud.Count),
            new Rate(genuine.Count(s => FlaggedAt(s.Alerts, ruleSet) is not null), genuine.Count),
            new Rate(ato.Count(s => FlaggedAt(s.Alerts, ruleSet) < FirstBankChange(s.Account)), ato.Count),
            precision,
            personas);
    }

    /// <summary>Each Account with the Alerts detection raises on it, which is also what the Agent Sample is drawn from.</summary>
    public static IReadOnlyList<ScoredAccount> Score(
        IEnumerable<LabelledAccount> dataset, RuleSet ruleSet, IReadOnlyDictionary<string, IReadOnlyList<RuleHit>>? graphHits = null) =>
        // Evaluation passes no stored Alerts: each Account is replayed from its whole timeline at once.
        [.. dataset.Select(a => new ScoredAccount(a, Detector.Detect(a.Events, ruleSet, graphHits: graphHits?.GetValueOrDefault(a.AccountId))))];

    private static DateTimeOffset? FlaggedAt(IReadOnlyList<Alert> alerts, RuleSet ruleSet) =>
        alerts.Select(a => CrossedHighAt(a, ruleSet)).Min();

    // Replays the hits in trigger order; a Score only grows, so the first running total at High is the crossing.
    private static DateTimeOffset? CrossedHighAt(Alert alert, RuleSet ruleSet)
    {
        var score = 0;
        foreach (var hit in alert.Hits.OrderBy(h => h.TriggeredAt))
        {
            score += hit.Points;
            if (score >= ruleSet.Bands.High) return hit.TriggeredAt;
        }
        return null;
    }

    // An Account that never changed bank details is early if flagged at all, so MaxValue stands in for "never".
    private static DateTimeOffset FirstBankChange(LabelledAccount account) =>
        account.Events.Where(e => e.Type == EventType.BankDetailsChanged).Select(e => e.OccurredAt).DefaultIfEmpty(DateTimeOffset.MaxValue).Min();
}
