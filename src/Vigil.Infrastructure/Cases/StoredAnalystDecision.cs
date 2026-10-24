using Vigil.Domain.Cases;

namespace Vigil.Infrastructure.Cases;

/// <summary>
/// An Analyst Decision as Act recorded it. Keyed by CaseId, so the database itself refuses a second decision on a
/// Case, which settles two analysts deciding at once. Outcome and action are kept as text because their enums belong
/// to Vigil.Agents, which Infrastructure does not reference.
/// </summary>
public sealed class StoredAnalystDecision
{
    public Guid CaseId { get; init; }
    public required string Outcome { get; init; }
    public required string Action { get; init; }
    public required string AnalystId { get; init; }
    public string? Notes { get; init; }
    public DateTimeOffset DecidedAt { get; init; }
}

/// <summary>The instruction Act recorded for the portal to enforce; Vigil enforces nothing. At most one per Case.</summary>
public sealed class StoredIntervention
{
    public Guid CaseId { get; init; }
    public required string AccountId { get; init; }
    public InterventionKind Kind { get; init; }

    /// <summary>Set for BlockBankChanges only: the block lifts at this time.</summary>
    public DateTimeOffset? Until { get; init; }

    public DateTimeOffset RecordedAt { get; init; }
}
