using Vigil.Agents;
using Vigil.Domain.Cases;
using Vigil.Infrastructure.Cases;

namespace Vigil.Api.Cases;

/// <summary>
/// The Act step until Stage 3.7 records Interventions, Audit Entries and Confirmed Fraud: the decision closes the Case.
/// </summary>
public sealed class CloseCaseActStep(CaseStore cases) : IActStep
{
    public async Task ActAsync(Guid caseId, AnalystDecision decision, CancellationToken ct)
    {
        var @case = await cases.FindAsync(caseId, ct) ?? throw new InvalidOperationException($"Case {caseId} not found.");
        @case.Status = CaseStatus.Closed;
        await cases.SaveAsync(ct);
    }
}
