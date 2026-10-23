using Neo4j.Driver;
using Vigil.Domain.Events;

namespace Vigil.Graph;

/// <summary>
/// Copies Portal Events into Neo4j as the Projection: Holder OWNS Account, and Account linked to each
/// identifier it used. Every write is a MERGE, so projecting the same events twice changes nothing.
/// </summary>
public sealed class GraphProjector(IDriver driver)
{
    /// <summary>
    /// Without a uniqueness constraint, two concurrent MERGEs of the same key can both miss and both create a node.
    /// The constraint also gives MERGE an index to look the key up by. Safe to run on every start.
    /// </summary>
    public async Task EnsureConstraintsAsync(CancellationToken ct)
    {
        var keys = new[] { ("Account", "accountId"), ("Holder", "holderId") }
            .Concat(IdentifierKind.All.Select(kind => (kind.Label, "value")));

        foreach (var (label, key) in keys)
            await driver.ExecutableQuery($"CREATE CONSTRAINT IF NOT EXISTS FOR (n:{label}) REQUIRE n.{key} IS UNIQUE").ExecuteAsync(ct);
    }

    public async Task ProjectAsync(IReadOnlyCollection<PortalEvent> batch, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // One transaction per batch, so a failure leaves no half-projected batch behind; the driver retries transient errors.
        await using var session = driver.AsyncSession();
        await session.ExecuteWriteAsync(async tx =>
        {
            await tx.RunAsync(Owns, new { rows = batch.Select(e => Row(e, value: null)).ToList() });

            foreach (var kind in IdentifierKind.All)
            {
                // A blank value would become one node that every Account sending a blank shares, linking strangers.
                var rows = batch
                    .Select(e => (Event: e, Value: kind.ValueOf(e)))
                    .Where(x => !string.IsNullOrWhiteSpace(x.Value))
                    .Select(x => Row(x.Event, x.Value))
                    .ToList();
                if (rows.Count > 0) await tx.RunAsync(kind.Cypher, new { rows });
            }
        });
    }

    private const string Owns = """
        UNWIND $rows AS row
        MERGE (h:Holder {holderId: row.holderId})
        MERGE (a:Account {accountId: row.accountId})
        MERGE (h)-[r:OWNS]->(a)
        """ + IdentifierKind.KeepEarliestLink;

    private static Dictionary<string, object?> Row(PortalEvent e, string? value) => new()
    {
        ["eventId"] = e.EventId.ToString(),
        ["accountId"] = e.AccountId,
        ["holderId"] = e.HolderId,
        ["at"] = e.OccurredAt,
        ["value"] = value,
    };
}
