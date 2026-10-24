using Microsoft.Agents.AI.Workflows;

namespace Vigil.Agents;

// Checkpoints name each executor by its id and its type's assembly-qualified name, so renaming or moving one of these
// classes, or changing an id, strands every Case already waiting in AwaitingApproval (ADR-0005).

/// <summary>
/// An Enrichment holds live objects, the masker and the ref table the tools add to, which cannot go into a checkpoint.
/// So it passes from Enrich to Investigate by reference. That is safe because a workflow is built per run and is only
/// ever resumed from Await Approval, after both steps are done.
/// </summary>
internal sealed class EnrichmentHandoff
{
    public Enrichment? Enrichment { get; set; }
}

internal sealed class EnrichExecutor(IInvestigationSource source, EnrichmentHandoff handoff)
    : Executor<InvestigationStarted, Enriched>(InvestigationWorkflow.EnrichId)
{
    public override async ValueTask<Enriched> HandleAsync(InvestigationStarted message, IWorkflowContext context, CancellationToken ct = default)
    {
        handoff.Enrichment = await Enrichment.CreateAsync(message.Alert, source, ct);
        return new Enriched(message.CaseId);
    }
}

/// <summary>Investigate, Validate and Review, with the retry, all inside the loop; its result becomes the approval request.</summary>
internal sealed class InvestigateExecutor(InvestigationLoop loop, EnrichmentHandoff handoff)
    : Executor<Enriched, ApprovalRequest>(InvestigationWorkflow.InvestigateId)
{
    public override async ValueTask<ApprovalRequest> HandleAsync(Enriched message, IWorkflowContext context, CancellationToken ct = default)
    {
        var enrichment = handoff.Enrichment!;

        // The loop returns on every path, LLM failures included: an exception escaping here would end the run with
        // no approval request, and the Case would never reach the analyst.
        var outcome = await loop.RunAsync(enrichment, ct);

        return new ApprovalRequest(
            message.CaseId,
            outcome.Finding,
            outcome.ReviewFlagged,
            outcome.InvestigationFailed,
            outcome.RejectionReasons,
            enrichment.Evidence.Masker.Map,
            enrichment.Evidence.Refs.Entries.ToDictionary(e => e.Key, e => e.Value),
            outcome.Usage,
            InvestigationWorkflow.PromptVersion);
    }
}

/// <summary>Runs on the analyst's decision, which the approval port delivers once the workflow is resumed.</summary>
[YieldsOutput(typeof(AnalystDecision))]
internal sealed class ActExecutor(Guid caseId, IActStep act) : Executor<AnalystDecision>(InvestigationWorkflow.ActId)
{
    public override async ValueTask HandleAsync(AnalystDecision decision, IWorkflowContext context, CancellationToken ct = default)
    {
        await act.ActAsync(caseId, decision, ct);
        await context.YieldOutputAsync(decision, ct);
    }
}
