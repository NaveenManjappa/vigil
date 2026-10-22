namespace Vigil.Domain.Detection;

public enum RiskBand
{
    Low,
    Medium,
    High,
    Critical,
}

/// <summary>One Rule matching on an Account, with the events that prove it.</summary>
/// <param name="TriggeredAt">OccurredAt of the event that completed the pattern, so the moment a band was crossed can be derived.</param>
public sealed record RuleHit(
    string RuleId,
    int Points,
    IReadOnlyList<Guid> EvidenceEventIds,
    DateTimeOffset TriggeredAt,
    string Explanation);

/// <summary>Detection's verdict on an Account: its Rule Hits, the Score they add up to, and its Risk Band.</summary>
public sealed record Alert(
    string AccountId,
    int Score,
    RiskBand Band,
    string RulesVersion,
    IReadOnlyList<RuleHit> Hits);
