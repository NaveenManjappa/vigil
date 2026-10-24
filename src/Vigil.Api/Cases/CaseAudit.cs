using Vigil.Domain.Audit;
using Vigil.Infrastructure.Audit;
using Vigil.Infrastructure.Cases;

namespace Vigil.Api.Cases;

/// <summary>An Audit Entry as the auditor reads it, its refs resolved to the EventIds they stood for when it was written.</summary>
public sealed record AuditEntryView(
    AuditEntryKind Kind,
    string Actor,
    string? Model,
    string? PromptVersion,
    string RulesVersion,
    IReadOnlyList<ResolvedEvidenceRef> Refs,
    long InputTokens,
    long OutputTokens,
    string? Detail,
    DateTimeOffset At)
{
    public static AuditEntryView From(StoredAuditEntry a) => new(
        a.Kind, a.Actor, a.Model, a.PromptVersion, a.RulesVersion,
        [.. a.Refs.Select(r => new ResolvedEvidenceRef(r.Ref, r.EventIds))],
        a.InputTokens, a.OutputTokens, a.Detail, a.At);
}

internal static class CaseAudit
{
    /// <summary>The refs the Case's Finding cites, in its order, with their EventIds; none when there is no Finding.</summary>
    public static List<AuditRef> CitedRefs(StoredCase @case) =>
        @case.Finding is null ? [] : [.. Resolve(@case, Agents.Finding.FromJson(@case.Finding)).Select(r => new AuditRef(r.Ref, r.EventIds))];

    /// <summary>Each ref <paramref name="finding"/> cites, in its order, with the EventIds the Case says it stands for.</summary>
    public static IEnumerable<(string Ref, List<Guid> EventIds)> Resolve(StoredCase @case, Agents.Finding finding)
    {
        // Validation rejects a Finding citing a ref never issued, so an unknown one here stands for no events, not an error.
        var refs = @case.EvidenceRefs ?? [];
        return finding.EvidenceRefs.Select(r => (r, refs.GetValueOrDefault(r) ?? []));
    }
}
