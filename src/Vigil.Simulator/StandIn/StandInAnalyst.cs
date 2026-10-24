using Vigil.Domain.Datasets;

namespace Vigil.Simulator.StandIn;

/// <summary>
/// The body of <c>POST /cases/{id}/decision</c>. Outcome and Action are the API's enum names as text, so the simulator
/// stays free of Vigil.Agents and its LLM packages.
/// </summary>
public sealed record StandInDecision(string Outcome, string Action, string AnalystId, string? Notes);

/// <summary>One row of <c>GET /cases</c>, with only what the Stand-in Analyst reads.</summary>
public sealed record CaseListing(Guid Id, string AccountId, string Status);

public sealed record PlannedDecision(Guid CaseId, StandInDecision Decision);

/// <param name="NotReady">Cases still Open or Investigating: not decidable yet, or held back by the budget guard.</param>
/// <param name="Unlabelled">Accounts awaiting approval that the Dataset has no Label for, such as a Scenario's.</param>
public sealed record DecisionPlan(IReadOnlyList<PlannedDecision> ToDecide, int NotReady, IReadOnlyList<string> Unlabelled);

/// <summary>
/// The Stand-in Analyst: decides a Case from its Account's Label, so the Rule-Tuning Report has Analyst Decisions to
/// learn from at Dataset scale (ADR-0012).
/// </summary>
public static class StandInAnalyst
{
    public const string AnalystId = "stand-in";

    public static StandInDecision DecisionFor(Label label) => label.IsFraud
        ? new("ConfirmedFraud", "BlockBankChanges", AnalystId, $"Stand-in: Label is {label.Persona} ({label.FraudKind}).")
        : new("FalsePositive", "NoAction", AnalystId, $"Stand-in: Label is {label.Persona} (genuine).");

    public static DecisionPlan Plan(IEnumerable<CaseListing> cases, IReadOnlyDictionary<string, Label> labels)
    {
        var all = cases.ToList();
        var awaiting = all.Where(c => c.Status == "AwaitingApproval").ToList();

        return new DecisionPlan(
            [.. awaiting.Where(c => labels.ContainsKey(c.AccountId)).Select(c => new PlannedDecision(c.Id, DecisionFor(labels[c.AccountId])))],
            all.Count(c => c.Status is "Open" or "Investigating"),
            // Left for a human: deciding a Case without its ground truth would put a guess into the Rule-Tuning Report.
            [.. awaiting.Where(c => !labels.ContainsKey(c.AccountId)).Select(c => c.AccountId).Distinct()]);
    }
}
