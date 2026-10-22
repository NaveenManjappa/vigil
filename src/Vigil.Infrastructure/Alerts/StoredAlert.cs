using Vigil.Domain.Detection;

namespace Vigil.Infrastructure.Alerts;

/// <summary>Persistence shape of an <see cref="Alert"/>. Score and band are stored so the queue can filter and sort in SQL.</summary>
public sealed class StoredAlert
{
    public Guid AlertId { get; init; }
    public required string AccountId { get; init; }
    public int Score { get; set; }
    public RiskBand Band { get; set; }
    public required string RulesVersion { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<StoredRuleHit> Hits { get; init; } = [];
}

/// <summary>A stored <see cref="RuleHit"/>. Written once and never updated, so past evidence stays exactly as it was.</summary>
public sealed class StoredRuleHit
{
    public Guid Id { get; init; }
    public Guid AlertId { get; init; }
    public required string RuleId { get; init; }
    public int Points { get; init; }
    public List<Guid> EvidenceEventIds { get; init; } = [];
    public DateTimeOffset TriggeredAt { get; init; }
    public required string Explanation { get; init; }

    public static StoredRuleHit From(RuleHit hit) => new()
    {
        RuleId = hit.RuleId,
        Points = hit.Points,
        EvidenceEventIds = [.. hit.EvidenceEventIds],
        TriggeredAt = hit.TriggeredAt,
        Explanation = hit.Explanation,
    };

    public RuleHit ToDomain() => new(RuleId, Points, EvidenceEventIds, TriggeredAt, Explanation);
}
