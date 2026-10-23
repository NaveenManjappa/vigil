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
    public static List<AuditRef> CitedRefs(StoredCase @case)
    {
        if (@case.Finding is null) return [];

        var refs = @case.EvidenceRefs ?? [];
        return [.. Agents.Finding.FromJson(@case.Finding).Evidence.Select(r => new AuditRef(r, refs.GetValueOrDefault(r) ?? []))];
    }
}
