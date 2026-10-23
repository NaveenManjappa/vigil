namespace Vigil.Domain.Detection;

public enum RiskBand
{
    Low,
    Medium,
    High,
    Critical,
}

/// <param name="TriggeredAt">OccurredAt of the event that completed the pattern, so the moment a band was crossed can be derived.</param>
public sealed record RuleHit(
    string RuleId,
    int Points,
    IReadOnlyList<Guid> EvidenceEventIds,
    DateTimeOffset TriggeredAt,
    string Explanation);

/// <summary>
/// Detection's verdict on one Risk Episode, not on the Account as a whole, so a takeover that keeps going raises
/// one Alert whose Score grows rather than a stream of near-duplicates (ADR-0013).
/// </summary>
public sealed record Alert(
    string AccountId,
    int Score,
    RiskBand Band,
    string RulesVersion,
    IReadOnlyList<RuleHit> Hits);
