namespace Vigil.Domain.Cases;

/// <summary>
/// Where a Case is in its life: opened by detection, investigated by the agent, then decided and closed by an analyst.
/// Stored as text, so the order here carries no meaning in the database.
/// </summary>
public enum CaseStatus
{
    Open,
    AwaitingDecision,
    Closed,
}

/// <summary>What the analyst concluded about the Account.</summary>
public enum DecisionOutcome
{
    ConfirmedFraud,
    FalsePositive,
}
