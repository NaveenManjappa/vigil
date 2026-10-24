using Vigil.Agents;
using Vigil.Domain.Events;
using Vigil.Domain.Graph;
using Vigil.Graph;
using Vigil.Infrastructure.Events;

namespace Vigil.Api.Investigations;

/// <summary>What the Investigator's tools read: the event store for timelines and baselines, the Projection for the Neighbourhood.</summary>
public sealed class InvestigationSource(EventStore events, GraphNeighbourhood graph) : IInvestigationSource
{
    public Task<IReadOnlyList<PortalEvent>> GetEventsAsync(IReadOnlyCollection<Guid> eventIds, CancellationToken ct) =>
        events.GetManyAsync(eventIds, ct);

    public async Task<IReadOnlyList<PortalEvent>> GetTimelineAsync(string accountId, int days, CancellationToken ct)
    {
        var timeline = await events.GetTimelineAsync(accountId, ct);
        if (timeline.Count == 0) return timeline;

        var from = timeline[^1].OccurredAt.AddDays(-days);
        return [.. timeline.Where(e => e.OccurredAt >= from)];
    }

    public Task<Neighbourhood> GetNeighbourhoodAsync(string accountId, CancellationToken ct) => graph.ForAccountAsync(accountId, ct);

    public async Task<HolderBaseline> GetHolderBaselineAsync(string accountId, DateTimeOffset before, CancellationToken ct) =>
        HolderBaseline.From(await events.GetTimelineAsync(accountId, ct), before);
}
