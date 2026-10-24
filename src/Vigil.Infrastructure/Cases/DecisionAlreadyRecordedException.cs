namespace Vigil.Infrastructure.Cases;

/// <summary>Another Analyst Decision on the Case was recorded first; the API answers this with 409.</summary>
public sealed class DecisionAlreadyRecordedException(Guid caseId, Exception? inner = null)
    : InvalidOperationException($"Case {caseId} already has an Analyst Decision.", inner)
{
    public Guid CaseId { get; } = caseId;
}
