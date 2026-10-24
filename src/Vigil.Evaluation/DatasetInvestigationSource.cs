using Vigil.Agents;
using Vigil.Domain.Datasets;
using Vigil.Domain.Events;
using Vigil.Domain.Graph;

namespace Vigil.Evaluation;

/// <summary>
/// What the Investigator's tools read during evaluation: the in-memory Dataset in place of the event store, and the
/// throwaway Neo4j the graph replay filled in place of the Projection (ADR-0016). It sees the Dataset as it stood when
/// the Case would have opened, so the agents never read an event, or a graph link, that came after.
/// </summary>
/// <param name="asOf">When the Case would have opened: the last Rule Hit of the Alert being investigated.</param>
/// <param name="graph">Reads a Neighbourhood from the replayed graph; null when the run has no Graph Rules.</param>
public sealed class DatasetInvestigationSource(
    IReadOnlyCollection<LabelledAccount> dataset,
    DateTimeOffset asOf,
    Func<string, CancellationToken, Task<Neighbourhood>>? graph) : IInvestigationSource
{
    private readonly Dictionary<Guid, PortalEvent> _events = dataset
        .SelectMany(a => a.Events)
        .Where(e => e.OccurredAt <= asOf)
        .ToDictionary(e => e.EventId);

    public Task<IReadOnlyList<PortalEvent>> GetEventsAsync(IReadOnlyCollection<Guid> eventIds, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<PortalEvent>>([.. eventIds.Where(_events.ContainsKey).Select(id => _events[id])]);

    public Task<IReadOnlyList<PortalEvent>> GetTimelineAsync(string accountId, int days, CancellationToken ct)
    {
        var timeline = Timeline(accountId);
        if (timeline.Count == 0) return Task.FromResult<IReadOnlyList<PortalEvent>>(timeline);

        var from = timeline[^1].OccurredAt.AddDays(-days);
        return Task.FromResult<IReadOnlyList<PortalEvent>>([.. timeline.Where(e => e.OccurredAt >= from)]);
    }

    public async Task<Neighbourhood> GetNeighbourhoodAsync(string accountId, CancellationToken ct)
    {
        if (graph is null) return new Neighbourhood(accountId, []);

        // The replay has already projected the whole Dataset, so links made after the Case opened are dropped here.
        var replayed = await graph(accountId, ct);
        return replayed with
        {
            Identifiers =
            [
                .. replayed.Identifiers
                    .Where(i => _events.ContainsKey(i.LinkEventId))
                    .Select(i => i with { OtherAccounts = [.. i.OtherAccounts.Where(a => _events.ContainsKey(a.LinkEventId))] }),
            ],
        };
    }

    public Task<HolderBaseline> GetHolderBaselineAsync(string accountId, DateTimeOffset before, CancellationToken ct) =>
        Task.FromResult(HolderBaseline.From(Timeline(accountId), before));

    private List<PortalEvent> Timeline(string accountId) =>
        [.. _events.Values.Where(e => e.AccountId == accountId).OrderBy(e => e.OccurredAt).ThenBy(e => e.EventId)];
}
