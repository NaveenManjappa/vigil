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

    public async Task<HolderBaseline> GetHolderBaselineAsync(string accountId, DateTimeOffset before, CancellationToken ct)
    {
        var usual = (await events.GetTimelineAsync(accountId, ct)).Where(e => e.OccurredAt < before).ToList();
        if (usual.Count == 0) return new HolderBaseline([], [], 0);

        var logins = usual.Count(e => e.Type == EventType.LoginSucceeded);
        // At least a week, so one login the day before the takeover does not read as a heavy user.
        var weeks = Math.Max(1, (before - usual[0].OccurredAt).TotalDays / 7);
        return new HolderBaseline(MostUsedFirst(usual, e => e.Country), MostUsedFirst(usual, e => e.DeviceId), Math.Round(logins / weeks, 1));
    }

    private static List<string> MostUsedFirst(IEnumerable<PortalEvent> events, Func<PortalEvent, string?> valueOf) =>
        [.. events.Select(valueOf).OfType<string>().CountBy(v => v).OrderByDescending(c => c.Value).ThenBy(c => c.Key).Select(c => c.Key)];
}
