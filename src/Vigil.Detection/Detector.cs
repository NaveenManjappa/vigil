using Vigil.Domain.Detection;
using Vigil.Domain.Events;

namespace Vigil.Detection;

/// <summary>
/// The rules engine's single entry point. A pure function: the same timeline, rule set and stored Alerts
/// always give the same Alerts, with no database and no clock, so the API and evaluation share it unchanged.
/// </summary>
public static class Detector
{
    /// <param name="stored">
    /// The Account's Alerts as already persisted. They are only ever extended: the result starts with them,
    /// in the same order and with their hits unchanged, followed by any new Alerts. Evaluation passes none.
    /// </param>
    public static IReadOnlyList<Alert> Detect(IReadOnlyList<PortalEvent> timeline, RuleSet ruleSet, IReadOnlyList<Alert>? stored = null)
    {
        stored ??= [];

        // Order by when events happened at the source, never by arrival (PROJECT.md Section 7.4).
        var ordered = timeline
            .DistinctBy(e => e.EventId)
            .OrderBy(e => e.OccurredAt).ThenBy(e => e.EventId)
            .ToList();
        if (ordered.Count == 0) return stored;

        // Every match is a candidate; episode assembly decides which ones count.
        var candidates = new List<RuleHit>();
        for (var i = 0; i < ordered.Count; i++)
        {
            foreach (var rule in ruleSet.Rules)
            {
                var hit = rule.Kind switch
                {
                    RuleKind.Sequence => SequenceMatcher.Match(rule, ordered, i),
                    RuleKind.FirstSighting => FirstSighting.Match(rule, ordered, i),
                    _ => throw new ArgumentOutOfRangeException(nameof(rule), rule.Kind, null),
                };
                if (hit is not null) candidates.Add(hit);
            }
        }

        var episodes = RiskEpisodes.Assemble(stored.Select(a => a.Hits), candidates, TimeSpan.FromDays(ruleSet.RiskPeriodDays));

        // A stored Alert keeps the Rules Version that opened it; only new Alerts take the current one.
        return episodes
            .Select((hits, i) => ToAlert(ordered[0].AccountId, i < stored.Count ? stored[i].RulesVersion : ruleSet.Version, hits, ruleSet))
            .ToList();
    }

    // Recomputed from the hits rather than stored, so a Score can only grow as hits are appended.
    private static Alert ToAlert(string accountId, string rulesVersion, List<RuleHit> hits, RuleSet ruleSet)
    {
        var score = hits.Sum(h => h.Points);
        return new Alert(accountId, score, ruleSet.Bands.BandFor(score), rulesVersion, hits);
    }
}
