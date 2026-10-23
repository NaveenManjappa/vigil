namespace Vigil.Agents;

/// <summary>
/// The Act step: the only place an Analyst Decision changes anything, and it runs only after one is made. Outside
/// Vigil.Agents because what it changes (the Case, Interventions, Audit Entries) lives in the API's stores.
/// </summary>
public interface IActStep
{
    Task ActAsync(Guid caseId, AnalystDecision decision, CancellationToken ct);
}
