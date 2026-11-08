using Neo4j.Driver;
using Vigil.Domain.Events;

namespace Vigil.Graph;

/// <summary>
/// Copies each Portal Event into Neo4j: Holder OWNS Account, and Account linked to each identifier the event carries.
/// Every write is a MERGE, so projecting the same event twice changes nothing.
/// </summary>
public sealed class GraphProjector(IDriver driver)
{
    /// <summary>One node per Account, Holder and identifier value. Safe to run on every start.</summary>
    public async Task EnsureConstraintsAsync(CancellationToken ct)
    {
        var keys = new[] { ("Account", "accountId"), ("Holder", "holderId") }
            .Concat(IdentifierKind.All.Select(kind => (kind.Label, "value")));

        foreach (var (label, key) in keys)
            await driver.ExecutableQuery($"CREATE CONSTRAINT IF NOT EXISTS FOR (n:{label}) REQUIRE n.{key} IS UNIQUE").ExecuteAsync(ct);
    }

    public async Task ProjectAsync(PortalEvent e, CancellationToken ct)
    {
        await driver.ExecutableQuery("""
                MERGE (h:Holder {holderId: $holderId})
                MERGE (a:Account {accountId: $accountId})
                MERGE (h)-[:OWNS]->(a)
                """)
            .WithParameters(new { holderId = e.HolderId, accountId = e.AccountId })
            .ExecuteAsync(ct);

        foreach (var kind in IdentifierKind.All)
        {
            // A blank value would become one node that every Account sending a blank shares, linking strangers.
            var value = kind.ValueOf(e);
            if (string.IsNullOrWhiteSpace(value)) continue;

            await driver.ExecutableQuery(kind.MergeCypher)
                .WithParameters(new { accountId = e.AccountId, value })
                .ExecuteAsync(ct);
        }
    }

    /// <summary>Marks an Account as Confirmed Fraud, which G2 looks for on every Account that shares an identifier with it.</summary>
    public async Task MarkConfirmedFraudAsync(string accountId, CancellationToken ct) =>
        await driver.ExecutableQuery("MERGE (a:Account {accountId: $accountId}) SET a.confirmedFraud = true")
            .WithParameters(new { accountId })
            .ExecuteAsync(ct);

    /// <summary>Empties the graph, for starting the walkthrough again.</summary>
    public async Task DeleteAllAsync(CancellationToken ct) =>
        await driver.ExecutableQuery("MATCH (n) DETACH DELETE n").ExecuteAsync(ct);
}
