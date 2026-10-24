using Vigil.Domain.Events;
using Vigil.Domain.Graph;

namespace Vigil.Agents;

/// <summary>
/// Everything an Investigation reads from outside Vigil.Agents, raw and unmasked. Masking happens on this side of the
/// interface, so no implementation can forget it.
/// </summary>
public interface IInvestigationSource
{
    /// <summary>The events a Rule Hit cites, which for a Graph Rule include other Accounts' events.</summary>
    Task<IReadOnlyList<PortalEvent>> GetEventsAsync(IReadOnlyCollection<Guid> eventIds, CancellationToken ct);

    /// <summary>
    /// The Account's events in the <paramref name="days"/> days up to its latest event, in OccurredAt order. Counted in
    /// event time, not wall clock, so a replayed Scenario reads the same as a live one.
    /// </summary>
    Task<IReadOnlyList<PortalEvent>> GetTimelineAsync(string accountId, int days, CancellationToken ct);

    Task<Neighbourhood> GetNeighbourhoodAsync(string accountId, CancellationToken ct);

    /// <summary>The Holder's usual behaviour from events before <paramref name="before"/>, when the Alert's evidence began.</summary>
    Task<HolderBaseline> GetHolderBaselineAsync(string accountId, DateTimeOffset before, CancellationToken ct);
}

/// <summary>What is normal for the Holder, so the Investigator can contrast a takeover with it.</summary>
public sealed record HolderBaseline(IReadOnlyList<string> UsualCountries, IReadOnlyList<string> UsualDevices, double LoginsPerWeek)
{
    /// <summary>The baseline from an Account's timeline, counting only events before <paramref name="before"/>.</summary>
    public static HolderBaseline From(IEnumerable<PortalEvent> timeline, DateTimeOffset before)
    {
        var usual = timeline.Where(e => e.OccurredAt < before).OrderBy(e => e.OccurredAt).ToList();
        if (usual.Count == 0) return new HolderBaseline([], [], 0);

        var logins = usual.Count(e => e.Type == EventType.LoginSucceeded);
        // At least a week, so one login the day before the takeover does not read as a heavy user.
        var weeks = Math.Max(1, (before - usual[0].OccurredAt).TotalDays / 7);
        return new HolderBaseline(MostUsedFirst(usual, e => e.Country), MostUsedFirst(usual, e => e.DeviceId), Math.Round(logins / weeks, 1));
    }

    private static List<string> MostUsedFirst(IEnumerable<PortalEvent> events, Func<PortalEvent, string?> valueOf) =>
        [.. events.Select(valueOf).OfType<string>().CountBy(v => v).OrderByDescending(c => c.Value).ThenBy(c => c.Key).Select(c => c.Key)];
}
