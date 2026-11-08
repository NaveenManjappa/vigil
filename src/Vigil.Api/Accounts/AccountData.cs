using Microsoft.EntityFrameworkCore;
using Vigil.Agents;
using Vigil.Api.Data;
using Vigil.Domain.Events;
using Vigil.Domain.Graph;
using Vigil.Graph;

namespace Vigil.Api.Accounts;

/// <summary>The one order every timeline uses, so an event's number is the same in the API and in the Investigator's brief.</summary>
public static class Timeline
{
    // By when it happened at the source, never by arrival; EventId only breaks ties.
    public static List<PortalEvent> Order(IEnumerable<PortalEvent> events) =>
        [.. events.OrderBy(e => e.OccurredAt).ThenBy(e => e.EventId)];
}

/// <summary>What the Investigator's tools read: timelines from Postgres, linked Accounts from Neo4j.</summary>
public sealed class AccountData(VigilDbContext db, GraphNeighbourhood graph) : IAccountData
{
    public async Task<IReadOnlyList<PortalEvent>> GetTimelineAsync(string accountId, CancellationToken ct)
    {
        var stored = await db.Events.AsNoTracking().Where(e => e.AccountId == accountId).ToListAsync(ct);
        return Timeline.Order(stored.Select(e => e.ToDomain()));
    }

    public Task<Neighbourhood> GetLinkedAccountsAsync(string accountId, CancellationToken ct) => graph.ForAccountAsync(accountId, ct);
}
