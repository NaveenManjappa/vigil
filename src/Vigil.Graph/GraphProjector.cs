using Neo4j.Driver;
using Vigil.Domain.Events;
using Vigil.Domain.Fraud;
using Vigil.Domain.Telemetry;

namespace Vigil.Graph;

/// <summary>
/// Copies Portal Events into Neo4j as the Projection: Holder OWNS Account, and Account linked to each
/// identifier it used. Every write is a MERGE, so projecting the same events twice changes nothing.
/// </summary>
public sealed class GraphProjector(IDriver driver)
{
    // Small enough that each batch's write transaction, and each chunk of a wipe, stays a modest size in memory.
    public const int RebuildBatchSize = 1000;
    private const int WipeChunkSize = 10000;

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
        using var span = VigilTelemetry.Source.StartActivity("graph.project");
        span?.SetTag("vigil.events", batch.Count);

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

    /// <summary>
    /// Empties Neo4j, constraints included, and projects everything again from the source of truth. Run with
    /// ingestion stopped (ADR-0015).
    /// </summary>
    /// <returns>How many events were projected.</returns>
    public async Task<int> RebuildAsync(
        IAsyncEnumerable<PortalEvent> events, IReadOnlyCollection<ConfirmedFraud> frauds, CancellationToken ct)
    {
        await WipeAsync(ct);
        await EnsureConstraintsAsync(ct);

        // Projection keeps the earliest link whatever order events come in, so no ordering is needed here.
        var projected = 0;
        await foreach (var batch in events.Chunk(RebuildBatchSize).WithCancellation(ct))
        {
            await ProjectAsync(batch, ct);
            projected += batch.Length;
        }

        // After the events, so each Case finds its Account already linked, as it does in ingestion.
        foreach (var fraud in frauds) await ProjectConfirmedFraudAsync(fraud, ct);
        return projected;
    }

    // Constraints are dropped too, so a rebuild after the projection model changes leaves none for old labels.
    private async Task WipeAsync(CancellationToken ct)
    {
        // One DETACH DELETE of the whole graph would hold every deletion in memory until commit. IN TRANSACTIONS deletes
        // in chunks, but only runs in an auto-commit transaction, which session.RunAsync gives and ExecutableQuery does not.
        await using (var session = driver.AsyncSession())
        {
            var cursor = await session.RunAsync($"MATCH (n) CALL (n) {{ DETACH DELETE n }} IN TRANSACTIONS OF {WipeChunkSize} ROWS");
            await cursor.ConsumeAsync();
        }

        var constraints = await driver.ExecutableQuery("SHOW CONSTRAINTS YIELD name").ExecuteAsync(ct);
        foreach (var row in constraints.Result)
            await driver.ExecutableQuery($"DROP CONSTRAINT `{row["name"].As<string>()}`").ExecuteAsync(ct);
    }

    /// <summary>
    /// Marks an Account as known fraud with a Case that CONCERNS it, which G2 looks for. Recording it twice keeps the
    /// first decision. MERGE on a pattern has no uniqueness constraint to lean on, so callers hold the Account's lock.
    /// </summary>
    public async Task ProjectConfirmedFraudAsync(ConfirmedFraud fraud, CancellationToken ct) =>
        await driver.ExecutableQuery(ConfirmedFraudCase)
            .WithParameters(new { accountId = fraud.AccountId, decidedAt = fraud.DecidedAt })
            .ExecuteAsync(ct);

    private const string ConfirmedFraudCase = """
        MERGE (a:Account {accountId: $accountId})
        MERGE (c:Case {outcome: 'ConfirmedFraud'})-[:CONCERNS]->(a)
          ON CREATE SET c.decidedAt = $decidedAt
        """;

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
