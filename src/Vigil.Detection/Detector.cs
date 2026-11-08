using Vigil.Domain.Detection;
using Vigil.Domain.Events;

namespace Vigil.Detection;

/// <summary>
/// The rules engine. It answers one question: which Rules did this new event trigger? It is a pure function, with no
/// database and no clock, so the same event and history always give the same hits.
/// </summary>
public static class Detector
{
    /// <param name="newEvent">The event that just arrived.</param>
    /// <param name="history">The Account's other events, in any order. Only those that happened before the new event count.</param>
    public static IReadOnlyList<RuleHit> Detect(PortalEvent newEvent, IReadOnlyList<PortalEvent> history, RuleSet ruleSet)
    {
        // Ordered by when events happened at the source, never by arrival (PROJECT.md Section 7.4).
        var earlier = history
            .Where(e => e.EventId != newEvent.EventId && e.OccurredAt <= newEvent.OccurredAt)
            .OrderBy(e => e.OccurredAt)
            .ToList();

        var hits = new List<RuleHit>();
        foreach (var rule in ruleSet.Rules)
        {
            var hit = rule.Kind switch
            {
                RuleKind.Sequence => SequenceMatcher.Match(rule, newEvent, earlier),
                RuleKind.FirstSighting => FirstSighting.Match(rule, newEvent, earlier),
                RuleKind.Graph => null, // Needs other Accounts, so Vigil.Graph checks it.
                _ => throw new ArgumentOutOfRangeException(nameof(ruleSet), rule.Kind, null),
            };
            if (hit is not null) hits.Add(hit);
        }

        return hits;
    }
}
