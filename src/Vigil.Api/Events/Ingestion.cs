using Microsoft.EntityFrameworkCore;
using Vigil.Detection;
using Vigil.Domain.Detection;
using Vigil.Domain.Events;
using Vigil.Domain.Fraud;
using Vigil.Graph;
using Vigil.Infrastructure;
using Vigil.Infrastructure.Alerts;
using Vigil.Infrastructure.Events;
using Vigil.Infrastructure.Fraud;

namespace Vigil.Api.Events;

/// <summary>
/// Takes in what Vigil learns, a batch of Portal Events or a Confirmed Fraud, writes it to Postgres and the graph,
/// and scores every Account it can change, in one transaction, so an Alert exists as soon as the request returns
/// and a failure leaves nothing half-written.
/// </summary>
public sealed class Ingestion(
    VigilDbContext db, EventStore events, AlertStore alerts, ConfirmedFraudStore frauds,
    GraphProjector graph, GraphRules graphRules, RuleSet ruleSet)
{
    public async Task<AppendResult> IngestAsync(IReadOnlyCollection<PortalEvent> batch, CancellationToken ct)
    {
        // Sorted so concurrent batches touching the same Accounts take their locks in the same order and cannot deadlock.
        var accounts = batch.Select(e => e.AccountId).Distinct().Order(StringComparer.Ordinal).ToList();

        return await InTransactionAsync(async () =>
        {
            foreach (var accountId in accounts) await db.LockAccountAsync(accountId, ct);

            var result = await events.AppendAsync(batch, ct);

            // Before the commit, so a Neo4j failure fails the request and the client's retry converges both stores.
            // The whole batch is projected, not just the new events: MERGE makes re-projecting a duplicate a no-op.
            await graph.ProjectAsync(batch, ct);

            // A new link can give an Account outside the batch a Graph Rule Hit, such as the first Account on a shared
            // bank account, so they are re-checked too. Their locks come after the batch's, so two batches that link
            // to each other's Accounts can deadlock; Postgres aborts one and the execution strategy retries it (ADR-0015).
            var neighbours = await graphRules.NeighboursOfAsync(batch, ct);
            await ScoreAsync(batchAccounts: accounts, neighbours, ct);
            return result;
        }, ct);
    }

    /// <summary>Idempotent: an Account already Confirmed Fraud keeps its first decision, which is returned.</summary>
    public async Task<ConfirmedFraud> RecordConfirmedFraudAsync(ConfirmedFraud fraud, CancellationToken ct) =>
        await InTransactionAsync(async () =>
        {
            // The Account's lock also serialises the Case MERGE, which no uniqueness constraint protects.
            await db.LockAccountAsync(fraud.AccountId, ct);
            var recorded = await frauds.RecordAsync(fraud, ct);

            // Projected even when already recorded, so a retry after a failed commit still reaches the graph.
            await graph.ProjectConfirmedFraudAsync(recorded, ct);

            // The decision changes no link and no score of its own, only its Neighbours' G2, so only they are scored.
            var neighbours = await graphRules.NeighboursOfAccountAsync(fraud.AccountId, ct);
            await ScoreAsync(batchAccounts: [], neighbours, ct);
            return recorded;
        }, ct);

    // Aspire's Npgsql integration retries transient failures, which requires the whole transaction to be replayable.
    private async Task<T> InTransactionAsync<T>(Func<Task<T>> work, CancellationToken ct) =>
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var result = await work();
            await tx.CommitAsync(ct);
            return result;
        });

    // The batch's Accounts are locked by the caller before anything is written; neighbours are only known once the
    // graph is projected, so their locks are taken here, after the batch's (ADR-0015).
    private async Task ScoreAsync(IReadOnlyList<string> batchAccounts, IReadOnlyList<string> neighbours, CancellationToken ct)
    {
        foreach (var accountId in neighbours) await db.LockAccountAsync(accountId, ct);

        foreach (var accountId in batchAccounts.Concat(neighbours))
        {
            var timeline = await events.GetTimelineAsync(accountId, ct);
            var graphHits = await graphRules.HitsForAsync(accountId, ruleSet, ct);
            await alerts.SaveAsync(accountId, stored => Detector.Detect(timeline, ruleSet, stored, graphHits), ct);
        }
    }
}
