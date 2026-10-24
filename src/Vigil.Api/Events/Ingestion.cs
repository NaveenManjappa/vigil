using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Vigil.Api.Cases;
using Vigil.Detection;
using Vigil.Domain.Detection;
using Vigil.Domain.Events;
using Vigil.Domain.Fraud;
using Vigil.Domain.Telemetry;
using Vigil.Graph;
using Vigil.Infrastructure;
using Vigil.Infrastructure.Alerts;
using Vigil.Infrastructure.Events;
using Vigil.Infrastructure.Fraud;

namespace Vigil.Api.Events;

/// <summary>
/// Takes in what Vigil learns, a batch of Portal Events or a Confirmed Fraud, writes it to Postgres and the graph,
/// and scores every Account it can change, in one transaction, so an Alert exists as soon as the request returns
/// and a failure leaves nothing half-written. Cases it opens are queued for Investigation only once that
/// transaction has committed, so a rolled-back batch never starts one.
/// </summary>
public sealed class Ingestion(
    VigilDbContext db, EventStore events, AlertStore alerts, ConfirmedFraudStore frauds,
    GraphProjector graph, GraphRules graphRules, RuleSet ruleSet, ICaseQueue caseQueue)
{
    public async Task<AppendResult> IngestAsync(IReadOnlyCollection<PortalEvent> batch, CancellationToken ct)
    {
        // Sorted so concurrent batches touching the same Accounts take their locks in the same order and cannot deadlock.
        var accounts = batch.Select(e => e.AccountId).Distinct().Order(StringComparer.Ordinal).ToList();

        return await InTransactionAsync(async opened =>
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
            await ScoreAsync(batchAccounts: accounts, neighbours, opened, ct);
            return result;
        }, ct);
    }

    /// <summary>Idempotent: an Account already Confirmed Fraud keeps its first decision, which is returned.</summary>
    /// <param name="first">
    /// Runs inside the same transaction, before anything is recorded, so its writes commit or roll back with the
    /// Confirmed Fraud; Act records the Analyst Decision this way. It may run more than once, as a retried attempt
    /// starts again with a cleared change tracker.
    /// </param>
    public async Task<ConfirmedFraud> RecordConfirmedFraudAsync(ConfirmedFraud fraud, CancellationToken ct, Func<Task>? first = null) =>
        await InTransactionAsync(async opened =>
        {
            // The Account's lock also serialises the Case MERGE, which no uniqueness constraint protects.
            await db.LockAccountAsync(fraud.AccountId, ct);
            if (first is not null) await first();
            var recorded = await frauds.RecordAsync(fraud, ct);

            // Projected even when already recorded, so a retry after a failed commit still reaches the graph.
            await graph.ProjectConfirmedFraudAsync(recorded, ct);

            // The decision changes no link and no score of its own, only its Neighbours' G2, so only they are scored.
            var neighbours = await graphRules.NeighboursOfAccountAsync(fraud.AccountId, ct);
            await ScoreAsync(batchAccounts: [], neighbours, opened, ct);
            return recorded;
        }, ct);

    // Aspire's Npgsql integration retries transient failures, which requires the whole transaction to be replayable,
    // so each attempt starts a fresh list of opened Cases and only the committed attempt's list is queued.
    private async Task<T> InTransactionAsync<T>(Func<List<Guid>, Task<T>> work, CancellationToken ct)
    {
        List<Guid> opened = [];
        var result = await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            opened = [];
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var attempt = await work(opened);
            await tx.CommitAsync(ct);
            return attempt;
        });

        // Carries this request's trace, so the Investigation the worker runs later can join it.
        var openedIn = Activity.Current?.Context ?? default;
        foreach (var caseId in opened) await caseQueue.EnqueueAsync(new QueuedCase(caseId, openedIn), ct);
        return result;
    }

    // The batch's Accounts are locked by the caller before anything is written; neighbours are only known once the
    // graph is projected, so their locks are taken here, after the batch's (ADR-0015).
    private async Task ScoreAsync(
        IReadOnlyList<string> batchAccounts, IReadOnlyList<string> neighbours, List<Guid> opened, CancellationToken ct)
    {
        foreach (var accountId in neighbours) await db.LockAccountAsync(accountId, ct);

        foreach (var accountId in batchAccounts.Concat(neighbours))
        {
            // Here rather than in the Detector, which stays a pure function that evaluation calls thousands of times.
            using var span = VigilTelemetry.Source.StartActivity("detect");
            span?.SetTag("vigil.account_id", accountId);

            var timeline = await events.GetTimelineAsync(accountId, ct);
            var graphHits = await graphRules.HitsForAsync(accountId, ruleSet, ct);
            var newCases = await alerts.SaveAsync(accountId, stored => Detector.Detect(timeline, ruleSet, stored, graphHits), ct);
            span?.SetTag("vigil.cases_opened", newCases.Count);
            opened.AddRange(newCases);
        }
    }
}
