using Vigil.Agents;
using Vigil.Domain.Cases;
using Vigil.Domain.Detection;

namespace Vigil.Api.Data;

/// <summary>
/// The work item an analyst picks up for one Alert. It moves Open → AwaitingDecision → Closed: the Investigator writes
/// a Finding, then an analyst makes the Analyst Decision. Each step fills in its own group of fields below.
/// </summary>
public sealed class Case
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string AccountId { get; init; }
    public CaseStatus Status { get; set; } = CaseStatus.Open;
    public DateTimeOffset OpenedAt { get; init; }

    public Guid AlertId { get; init; }
    public Alert Alert { get; init; } = null!;

    // Filled in by POST /cases/{id}/investigate.
    public Finding? Finding { get; set; }
    public string? Model { get; set; }
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public DateTimeOffset? InvestigatedAt { get; set; }

    // Filled in by POST /cases/{id}/decision.
    public DecisionOutcome? Outcome { get; set; }
    public Recommendation? Action { get; set; }
    public string? AnalystId { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }

    /// <summary>What the portal is told to do. Vigil records it; the portal enforces it.</summary>
    public InterventionKind? Intervention { get; set; }

    /// <summary>Set for BlockBankChanges only.</summary>
    public DateTimeOffset? InterventionUntil { get; set; }
}
