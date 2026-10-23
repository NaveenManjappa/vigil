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

    /// <summary>The Holder's usual behaviour from events before <paramref name="before"/>, when the flagged activity began.</summary>
    Task<HolderBaseline> GetHolderBaselineAsync(string accountId, DateTimeOffset before, CancellationToken ct);
}

/// <summary>What is normal for the Holder, so the Investigator can contrast a takeover with it.</summary>
public sealed record HolderBaseline(IReadOnlyList<string> UsualCountries, IReadOnlyList<string> UsualDevices, double LoginsPerWeek);
