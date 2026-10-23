namespace Vigil.Domain.Cases;

/// <summary>
/// Where a Case is in its life: opened by detection, investigated once, waiting for an Analyst Decision, then closed.
/// Stored as text, so the order here carries no meaning in the database.
/// </summary>
public enum CaseStatus
{
    Open,
    Investigating,
    AwaitingApproval,
    Closed,
}
