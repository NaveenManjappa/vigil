using Vigil.Domain.Detection;
using Vigil.Domain.Events;

namespace Vigil.Detection;

/// <summary>
/// The rules engine's single entry point. A pure function: the same timeline and rule set always
/// give the same Alerts, with no database and no clock, so the API and evaluation share it unchanged.
/// </summary>
public static class Detector
{
    public static IReadOnlyList<Alert> Detect(IReadOnlyList<PortalEvent> timeline, RuleSet ruleSet)
    {
        // Order by when events happened at the source, never by arrival (PROJECT.md Section 7.4).
        var ordered = timeline
            .DistinctBy(e => e.EventId)
            .OrderBy(e => e.OccurredAt).ThenBy(e => e.EventId)
            .ToList();

        var hits = new List<RuleHit>();
        for (var i = 0; i < ordered.Count; i++)
        {
            foreach (var rule in ruleSet.Rules)
            {
                // A rule counts once: its first hit is the evidence, repeats add nothing.
                if (hits.Any(h => h.RuleId == rule.Id)) continue;

                if (SequenceMatcher.Match(rule, ordered, i) is { } hit) hits.Add(hit);
            }
        }

        if (hits.Count == 0) return [];

        var score = hits.Sum(h => h.Points);
        return [new Alert(ordered[0].AccountId, score, ruleSet.Bands.BandFor(score), ruleSet.Version, hits)];
    }
}
