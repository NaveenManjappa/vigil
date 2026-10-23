using Vigil.Domain.Audit;

namespace Vigil.Infrastructure.Audit;

/// <summary>One cited Evidence Ref, resolved when the entry was written so it reads the same however long it is kept.</summary>
public sealed record AuditRef(string Ref, List<Guid> EventIds);

/// <summary>
/// One Audit Entry. Never updated or deleted: a trigger on the table refuses both, so the trail cannot be rewritten
/// even by code that bypasses <see cref="AuditStore"/>.
/// </summary>
public sealed class StoredAuditEntry
{
    /// <summary>Identity, so entries read back in the order they were written.</summary>
    public long Position { get; init; }

    public Guid CaseId { get; init; }
    public AuditEntryKind Kind { get; init; }

    /// <summary>The agent Id for an agent's step, the analyst ID for a human one.</summary>
    public required string Actor { get; init; }

    /// <summary>The model that answered; null where no model was involved.</summary>
    public string? Model { get; init; }

    public string? PromptVersion { get; init; }
    public required string RulesVersion { get; init; }

    /// <summary>The Evidence Refs the step relied on, in citation order, each with the EventIds it stands for.</summary>
    public List<AuditRef> Refs { get; init; } = [];

    public long InputTokens { get; init; }
    public long OutputTokens { get; init; }

    /// <summary>What the step concluded, in a line, such as the verdict or the outcome and action.</summary>
    public string? Detail { get; init; }

    public DateTimeOffset At { get; init; }
}
