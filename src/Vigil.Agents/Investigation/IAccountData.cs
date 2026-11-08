using Vigil.Domain.Events;
using Vigil.Domain.Graph;

namespace Vigil.Agents;

/// <summary>What the Investigator can read about an Account. The API implements it over Postgres and Neo4j.</summary>
public interface IAccountData
{
    /// <summary>The Account's events, oldest first. An event's number is its position in this list, starting at 1.</summary>
    Task<IReadOnlyList<PortalEvent>> GetTimelineAsync(string accountId, CancellationToken ct);

    Task<Neighbourhood> GetLinkedAccountsAsync(string accountId, CancellationToken ct);
}
