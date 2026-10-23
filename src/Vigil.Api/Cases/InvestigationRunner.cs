using System.Diagnostics;
using Vigil.Agents;
using Vigil.Domain.Audit;
using Vigil.Domain.Cases;
using Vigil.Infrastructure.Audit;
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
/// Runs the Investigation workflow up to Await Approval, then stores what it asks the analyst with the Case, with an
/// Audit Entry for each agent's step. The Finding is stored masked, beside the map that unmasks it (ADR-0008).
/// </summary>
public sealed class WorkflowInvestigationRunner(
    CaseStore cases, AuditStore audit, InvestigationWorkflow workflow, InvestigationMetrics metrics, TimeProvider clock) : IInvestigationRunner
{
    public async Task RunAsync(Guid caseId, CancellationToken ct)
    {
        var @case = await cases.FindWithAlertAsync(caseId, ct) ?? throw new InvalidOperationException($"Case {caseId} not found.");

        var started = clock.GetTimestamp();
        var request = await workflow.InvestigateAsync(caseId, @case.Alert.ToDomain(), ct);
        metrics.Record(request.Usage, clock.GetElapsedTime(started));

        // Written only once the paused run's checkpoint is saved, so an AwaitingApproval Case always has one to resume.
        @case.Finding = request.Finding?.ToJson();
        @case.ReviewFlagged = request.ReviewFlagged;
        @case.InvestigationFailed = request.InvestigationFailed;
        @case.PseudonymMap = new(request.PseudonymMap);
        @case.EvidenceRefs = request.EvidenceRefs.ToDictionary(r => r.Key, r => r.Value.ToList());
        @case.PromptVersion = request.PromptVersion;
        @case.InvestigationTrace = Activity.Current?.Id;
        @case.Status = CaseStatus.AwaitingApproval;

        // Saved with the Case in one transaction, so the entries exist exactly when the Case is awaiting approval.
        var now = clock.GetUtcNow();
        audit.Append(AgentEntry(@case, request, AuditEntryKind.InvestigationCompleted, Investigator.AgentId, Investigator.PromptVersion,
            request.Finding is { } f ? $"{f.Verdict}, {f.Confidence} confidence, recommends {f.Recommendation}" : "InvestigationFailed: no Finding", now));
        audit.Append(AgentEntry(@case, request, AuditEntryKind.ReviewCompleted, Reviewer.AgentId, Reviewer.PromptVersion,
            ReviewDetail(request), now));
        await cases.SaveAsync(ct);
    }

    // An agent may have been called twice, on the retry, so its tokens are summed over every call it made.
    private static StoredAuditEntry AgentEntry(
        StoredCase @case, ApprovalRequest request, AuditEntryKind kind, string agentId, string promptVersion, string detail, DateTimeOffset at)
    {
        var calls = request.Usage.Where(u => u.AgentId == agentId).ToList();
        return new StoredAuditEntry
        {
            CaseId = @case.CaseId,
            Kind = kind,
            Actor = agentId,
            Model = calls.LastOrDefault(c => c.Model is not null)?.Model,
            PromptVersion = promptVersion,
            RulesVersion = @case.Alert.RulesVersion,
            Refs = CaseAudit.CitedRefs(@case),
            InputTokens = calls.Sum(c => c.InputTokens),
            OutputTokens = calls.Sum(c => c.OutputTokens),
            Detail = detail,
            At = at,
        };
    }

    private static string ReviewDetail(ApprovalRequest request) =>
        request.InvestigationFailed ? "Not reviewed: the Investigation failed"
        : request.ReviewFlagged ? "Rejected after the retry: " + string.Join("; ", request.RejectionReasons)
        : "Approved";
}
