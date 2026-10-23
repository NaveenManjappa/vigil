using Vigil.Agents;
using Vigil.Domain.Cases;
using Vigil.Infrastructure.Cases;

namespace Vigil.Api.Cases;

/// <summary>
/// Runs one Case's Investigation, which the worker has already moved to Investigating, and leaves it in
/// AwaitingApproval. The seam the Investigation workflow plugs into.
/// </summary>
public interface IInvestigationRunner
{
    Task RunAsync(Guid caseId, CancellationToken ct);
}

/// <summary>
/// Runs the Investigation workflow up to Await Approval, then stores what it asks the analyst with the Case. The
/// Finding is stored masked, beside the map that unmasks it (ADR-0008).
/// </summary>
public sealed class WorkflowInvestigationRunner(CaseStore cases, InvestigationWorkflow workflow) : IInvestigationRunner
{
    public async Task RunAsync(Guid caseId, CancellationToken ct)
    {
        var @case = await cases.FindWithAlertAsync(caseId, ct) ?? throw new InvalidOperationException($"Case {caseId} not found.");

        var request = await workflow.InvestigateAsync(caseId, @case.Alert.ToDomain(), ct);

        // Written only once the paused run's checkpoint is saved, so an AwaitingApproval Case always has one to resume.
        @case.Finding = request.Finding?.ToJson();
        @case.ReviewFlagged = request.ReviewFlag;
        @case.InvestigationFailed = request.InvestigationFailed;
        @case.PseudonymMap = new(request.PseudonymMap);
        @case.EvidenceRefs = request.EvidenceRefs.ToDictionary(r => r.Key, r => r.Value.ToList());
        @case.PromptVersion = request.PromptVersion;
        @case.Status = CaseStatus.AwaitingApproval;
        await cases.SaveAsync(ct);
    }
}
