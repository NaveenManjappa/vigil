using Vigil.Domain.Cases;

namespace Vigil.Simulator.StandIn;

/// <summary>The Case statuses <c>GET /cases</c> returns as text.</summary>
public static class CaseStatuses
{
    public const string Open = nameof(CaseStatus.Open);
    public const string Investigating = nameof(CaseStatus.Investigating);
    public const string AwaitingApproval = nameof(CaseStatus.AwaitingApproval);
}

/// <summary>
/// The Analyst Decision values the API stores as text: its <c>DecisionOutcome</c> and <c>Recommendation</c> names,
/// restated here so the simulator stays free of Vigil.Agents and its LLM packages.
/// </summary>
public static class DecisionValues
{
    public const string ConfirmedFraud = "ConfirmedFraud";
    public const string FalsePositive = "FalsePositive";

    public const string BlockBankChanges = "BlockBankChanges";
    public const string NoAction = "NoAction";
}
