using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Checkpointing;
using Vigil.Domain.Detection;

namespace Vigil.Agents;

/// <summary>
/// A Case's Investigation as a MAF workflow: Enrich → Investigate → Await Approval → Act (ADR-0005). It runs until
/// Await Approval asks for an Analyst Decision, checkpointing every superstep, and stops there; a decision rebuilds
/// it, resumes from the Case's latest checkpoint and answers the re-emitted request, so the pause survives a restart.
/// </summary>
public sealed class InvestigationWorkflow(
    InvestigationLoop loop, IInvestigationSource source, IActStep act, JsonCheckpointStore checkpoints)
{
    // Part of every checkpoint: changing one strands Cases already waiting for a decision.
    internal const string EnrichId = "enrich";
    internal const string InvestigateId = "investigate";
    internal const string ApprovalPortId = "await-approval";
    internal const string ActId = "act";

    public const string PromptVersion = $"{Investigator.PromptVersion}+{Reviewer.PromptVersion}";

    /// <summary>Runs the Investigation up to Await Approval and returns what it asks the analyst.</summary>
    public async Task<ApprovalRequest> InvestigateAsync(Guid caseId, Alert alert, CancellationToken ct)
    {
        await using var run = await InProcessExecution.RunStreamingAsync(
            Build(caseId), new InvestigationStarted(caseId, alert), Checkpoints(), SessionId(caseId), ct);

        ApprovalRequest? request = null;
        // Not blocking on the pending request, the stream ends once the run is waiting on it, after the superstep
        // that asked, and so the checkpoint holding the request, is saved.
        await foreach (var evt in run.WatchStreamAsync(blockOnPendingRequest: false, ct))
        {
            if (evt is RequestInfoEvent asked && asked.Request.TryGetDataAs<ApprovalRequest>(out var data)) request = data;
            if (evt is WorkflowErrorEvent error) throw Failed(caseId, error);
        }

        return request ?? throw new InvalidOperationException($"The Investigation of Case {caseId} ended without asking for a decision.");
    }

    /// <summary>Resumes the paused Investigation with the analyst's decision and runs Act.</summary>
    public async Task DecideAsync(Guid caseId, AnalystDecision decision, CancellationToken ct)
    {
        var latest = await LatestCheckpointAsync(caseId);
        var manager = CheckpointManager.CreateJson(new DecisionCheckpointStore(checkpoints), ChatAgents.Json);

        await using var run = await InProcessExecution.ResumeStreamingAsync(Build(caseId), latest, manager, ct);

        // Blocking on the pending request this time, so the stream waits for the response sent below.
        await foreach (var evt in run.WatchStreamAsync(ct))
        {
            switch (evt)
            {
                // Re-emitted from the checkpoint, with the same RequestId it had before any restart.
                case RequestInfoEvent asked:
                    await run.SendResponseAsync(asked.Request.CreateResponse(decision));
                    break;
                case WorkflowOutputEvent:
                    return;
                case WorkflowErrorEvent error:
                    throw Failed(caseId, error);
            }
        }

        throw new InvalidOperationException($"The decision on Case {caseId} did not reach Act.");
    }

    private async Task<CheckpointInfo> LatestCheckpointAsync(Guid caseId)
    {
        var index = await checkpoints.RetrieveIndexAsync(SessionId(caseId));
        return index.LastOrDefault()
            ?? throw new InvalidOperationException($"Case {caseId} has no checkpoint to resume from.");
    }

    private Workflow Build(Guid caseId)
    {
        var handoff = new EnrichmentHandoff();
        var enrich = new EnrichExecutor(source, handoff);
        var investigate = new InvestigateExecutor(loop, handoff);
        var approval = RequestPort.Create<ApprovalRequest, AnalystDecision>(ApprovalPortId);
        var actOnDecision = new ActExecutor(caseId, act);

        return new WorkflowBuilder(enrich)
            .WithName("vigil-investigation")
            .AddEdge(enrich, investigate)
            .AddEdge(investigate, approval)
            .AddEdge(approval, actOnDecision)
            .WithOutputFrom(actOnDecision)
            // Workflow and executor spans, under whatever Activity is current when the run starts. Message payloads
            // stay out of the trace, as the prompts do.
            .WithOpenTelemetry(o => o.EnableSensitiveData = false)
            .Build();
    }

    // Built per run because a manager caches what it has read; the store underneath is what persists.
    private CheckpointManager Checkpoints() => CheckpointManager.CreateJson(checkpoints, ChatAgents.Json);

    private static string SessionId(Guid caseId) => caseId.ToString();

    private static InvalidOperationException Failed(Guid caseId, WorkflowErrorEvent error)
    {
        var cause = error.Exception?.InnerException ?? error.Exception;
        return new InvalidOperationException($"The Investigation workflow for Case {caseId} failed: {cause?.Message}", cause);
    }
}
