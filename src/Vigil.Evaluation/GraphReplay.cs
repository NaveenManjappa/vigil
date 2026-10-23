using Neo4j.Driver;
using Vigil.Domain.Datasets;
using Vigil.Domain.Detection;
using Vigil.Domain.Events;
using Vigil.Graph;

namespace Vigil.Evaluation;

/// <summary>
/// Finds each Account's candidate graph Rule Hits by replaying a Dataset into Neo4j the way ingestion would see it:
/// one event at a time in OccurredAt order, re-checking the event's Account and its Neighbours after each. A hit is
/// therefore found as soon as the link that completes it exists, and never from a link that formed later.
/// </summary>
public static class GraphReplay
{
    public static async Task<IReadOnlyDictionary<string, IReadOnlyList<RuleHit>>> CollectAsync(
        IDriver driver, IReadOnlyCollection<LabelledAccount> dataset, RuleSet ruleSet, CancellationToken ct)
    {
        var projector = new GraphProjector(driver);
        var rules = new GraphRules(driver);
        GraphRules.EnsureKnown(ruleSet);

        // Rebuilding from nothing empties whatever an earlier replay left and creates the constraints.
        await projector.RebuildAsync(AsyncEnumerable.Empty<PortalEvent>(), [], ct);

        var collected = dataset.ToDictionary(a => a.AccountId, _ => new List<RuleHit>());
        // The same hit comes back on every later re-check; it is kept as first found, so its explanation is as of then.
        var seen = new HashSet<string>();

        var timeline = dataset.SelectMany(a => a.Events).OrderBy(e => e.OccurredAt).ThenBy(e => e.EventId);
        foreach (var e in timeline)
        {
            PortalEvent[] batch = [e];
            await projector.ProjectAsync(batch, ct);

            foreach (var accountId in (await rules.NeighboursOfAsync(batch, ct)).Prepend(e.AccountId))
            {
                foreach (var hit in await rules.HitsForAsync(accountId, ruleSet, ct))
                    if (seen.Add($"{accountId}/{hit.RuleId}/{string.Join(',', hit.EvidenceEventIds)}"))
                        collected[accountId].Add(hit);
            }
        }

        return collected.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<RuleHit>)kv.Value);
    }
}
