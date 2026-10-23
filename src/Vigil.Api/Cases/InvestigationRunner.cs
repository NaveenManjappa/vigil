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

/// <summary>Until the agents exist, an Investigation finds nothing and hands the Case straight to the analyst.</summary>
public sealed class PassThroughInvestigationRunner(CaseStore cases) : IInvestigationRunner
{
    public async Task RunAsync(Guid caseId, CancellationToken ct)
    {
        var @case = await cases.FindAsync(caseId, ct) ?? throw new InvalidOperationException($"Case {caseId} not found.");
        @case.Status = CaseStatus.AwaitingApproval;
        await cases.SaveAsync(ct);
    }
}
