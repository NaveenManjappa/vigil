using Microsoft.Extensions.Options;
using Vigil.Agents;
using Vigil.Api.Events;
using Vigil.Domain.Audit;
using Vigil.Domain.Cases;
using Vigil.Domain.Fraud;
using Vigil.Infrastructure.Audit;
using Vigil.Infrastructure.Cases;

namespace Vigil.Api.Cases;

/// <summary>How long an Intervention lasts, bound from the <c>Intervention</c> config section.</summary>
public sealed class InterventionOptions
{
    /// <summary>Seven days, the block the analyst approves in Margaret's Case (PROJECT.md Section 3.1).</summary>
    public int BlockBankChangesDays { get; init; } = 7;
}

/// <summary>
/// The Act step: records the Analyst Decision, its Intervention and their Audit Entries, and closes the Case. It
/// enforces nothing; the portal reads the Intervention and acts on it. A ConfirmedFraud outcome also makes the
/// Account a Confirmed Fraud, in the same transaction, which re-scores its Neighbours.
/// </summary>
public sealed class ActStep(
    CaseStore cases, AuditStore audit, Ingestion ingestion, IOptions<InterventionOptions> options, TimeProvider clock) : IActStep
{
    public async Task ActAsync(Guid caseId, AnalystDecision decision, CancellationToken ct)
    {
        var decidedAt = clock.GetUtcNow();
        if (decision.Outcome != DecisionOutcome.ConfirmedFraud)
        {
            await RecordAsync(caseId, decision, decidedAt, ct);
            return;
        }

        var @case = await cases.FindWithAlertAsync(caseId, ct) ?? throw NotFound(caseId);
        var fraud = new ConfirmedFraud(@case.Alert.AccountId, decidedAt, ConfirmedFraudSource.Analyst);

        // The decision is written first, so a second, losing decision fails before anything reaches the graph.
        await ingestion.RecordConfirmedFraudAsync(fraud, ct, first: () => RecordAsync(caseId, decision, decidedAt, ct));
    }

    private async Task RecordAsync(Guid caseId, AnalystDecision decision, DateTimeOffset decidedAt, CancellationToken ct)
    {
        // Read here, not passed in: inside the Confirmed Fraud transaction a retried attempt starts from a cleared tracker.
        var @case = await cases.FindWithAlertAsync(caseId, ct) ?? throw NotFound(caseId);
        // The unique key only catches a decision still in flight; one already committed is loaded here, and
        // assigning over it would quietly replace the first analyst's decision.
        if (@case.Decision is not null) throw new DecisionAlreadyRecordedException(caseId);

        @case.Decision = new StoredAnalystDecision
        {
            CaseId = caseId,
            Outcome = decision.Outcome.ToString(),
            Action = decision.Action.ToString(),
            AnalystId = decision.AnalystId,
            Notes = decision.Notes,
            DecidedAt = decidedAt,
        };
        @case.Intervention = InterventionFor(decision.Action, @case.Alert.AccountId, decidedAt);
        @case.Status = CaseStatus.Closed;

        audit.Append(AnalystEntry(@case, decision, AuditEntryKind.DecisionRecorded, $"{decision.Outcome}, {decision.Action}", decidedAt));
        audit.Append(AnalystEntry(@case, decision, AuditEntryKind.ActionTaken, Describe(@case.Intervention), decidedAt));
        await cases.SaveDecisionAsync(caseId, ct);
    }

    private StoredIntervention? InterventionFor(Recommendation action, string accountId, DateTimeOffset at) => action switch
    {
        Recommendation.BlockBankChanges => new()
        {
            AccountId = accountId,
            Kind = InterventionKind.BlockBankChanges,
            Until = at.AddDays(options.Value.BlockBankChangesDays),
            RecordedAt = at,
        },
        Recommendation.ContactHolder => new() { AccountId = accountId, Kind = InterventionKind.ContactHolder, RecordedAt = at },
        _ => null,
    };

    private static string Describe(StoredIntervention? intervention) => intervention switch
    {
        null => "No Intervention",
        { Until: { } until } => $"{intervention.Kind} until {until:O}",
        _ => intervention.Kind.ToString(),
    };

    // The analyst is the actor of both: Act only carries out what they chose. No model is involved, so none is named.
    private static StoredAuditEntry AnalystEntry(
        StoredCase @case, AnalystDecision decision, AuditEntryKind kind, string detail, DateTimeOffset at) => new()
    {
        CaseId = @case.CaseId,
        Kind = kind,
        Actor = decision.AnalystId,
        PromptVersion = @case.PromptVersion,
        RulesVersion = @case.Alert.RulesVersion,
        Refs = CaseAudit.CitedRefs(@case),
        Detail = detail,
        At = at,
    };

    private static InvalidOperationException NotFound(Guid caseId) => new($"Case {caseId} not found.");
}
