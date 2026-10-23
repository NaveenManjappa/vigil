using Microsoft.EntityFrameworkCore;
using Vigil.Detection;
using Vigil.Domain.Detection;
using Vigil.Domain.Events;
using Vigil.Graph;
using Vigil.Infrastructure;
using Vigil.Infrastructure.Alerts;
using Vigil.Infrastructure.Events;

namespace Vigil.Api.Events;

/// <summary>
/// Appends a batch, projects it into the graph and scores every Account in it and every neighbour it links to,
/// in one transaction, so an Alert exists as soon as the request returns and a failure leaves neither events nor
/// Alerts half-written.
/// </summary>
public sealed class Ingestion(
    VigilDbContext db, EventStore events, AlertStore alerts, GraphProjector graph, GraphRules graphRules, RuleSet ruleSet)
{
    public async Task<AppendResult> IngestAsync(IReadOnlyCollection<PortalEvent> batch, CancellationToken ct)
    {
        // Sorted so concurrent batches touching the same Accounts take their locks in the same order and cannot deadlock.
        var accounts = batch.Select(e => e.AccountId).Distinct().Order(StringComparer.Ordinal).ToList();

        // Aspire's Npgsql integration retries transient failures, which requires the whole transaction to be replayable.
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            foreach (var accountId in accounts) await db.LockAccountAsync(accountId, ct);

            var result = await events.AppendAsync(batch, ct);

            // Before the commit, so a Neo4j failure fails the request and the client's retry converges both stores.
            // The whole batch is projected, not just the new events: MERGE makes re-projecting a duplicate a no-op.
            await graph.ProjectAsync(batch, ct);

            // A new link can give an Account outside the batch a Graph Rule Hit, such as the first Account on a shared
            // bank account, so they are re-checked too. Their locks come after the batch's, so two batches that link
            // to each other's Accounts can deadlock; Postgres aborts one and the execution strategy retries it (ADR-0015).
            var neighbours = await graphRules.NeighboursOfAsync(batch, ct);
            foreach (var accountId in neighbours) await db.LockAccountAsync(accountId, ct);

            foreach (var accountId in accounts.Concat(neighbours))
            {
                var timeline = await events.GetTimelineAsync(accountId, ct);
                var graphHits = await graphRules.HitsForAsync(accountId, ruleSet, ct);
                await alerts.SaveAsync(accountId, stored => Detector.Detect(timeline, ruleSet, stored, graphHits), ct);
            }

            await tx.CommitAsync(ct);
            return result;
        });
    }
}
