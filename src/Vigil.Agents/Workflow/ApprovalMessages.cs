using Vigil.Domain.Detection;

namespace Vigil.Agents;

public enum DecisionOutcome
{
    ConfirmedFraud,
    FalsePositive,
}

/// <summary>
/// The analyst's answer to Await Approval. The action is chosen from the same set the Investigator recommends from,
/// and may differ from its recommendation.
/// </summary>
public sealed record AnalystDecision(DecisionOutcome Outcome, Recommendation Action, string AnalystId, string? Notes);

/// <summary>
/// What Await Approval puts to the analyst: the Investigation's outcome, still masked, with what is needed to unmask
/// the Finding and resolve its refs once it is stored with the Case.
/// </summary>
/// <param name="PseudonymMap">Pseudonym to real identifier, for this Investigation only.</param>
/// <param name="EvidenceRefs">Every ref issued, with the EventIds it stands for.</param>
public sealed record ApprovalRequest(
    Guid CaseId,
    Finding? Finding,
    bool ReviewFlag,
    bool InvestigationFailed,
    IReadOnlyList<string> RejectionReasons,
    IReadOnlyDictionary<string, string> PseudonymMap,
    IReadOnlyDictionary<string, IReadOnlyList<Guid>> EvidenceRefs,
    IReadOnlyList<AgentUsage> Usage,
    string PromptVersion);

/// <summary>The workflow's input. The Alert travels in it, so Enrich needs nothing the source cannot give.</summary>
public sealed record InvestigationStarted(Guid CaseId, Alert Alert);

/// <summary>Enrich's signal that the Enrichment is ready; the Enrichment itself is handed over by reference.</summary>
public sealed record Enriched(Guid CaseId);
