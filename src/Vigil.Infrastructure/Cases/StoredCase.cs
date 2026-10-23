using Vigil.Domain.Cases;
using Vigil.Infrastructure.Alerts;

namespace Vigil.Infrastructure.Cases;

/// <summary>
/// The work item an analyst picks up for one Alert. Band and Score are read from the Alert rather than copied,
/// so an Alert that rises to Critical reprioritises its Case without the Case being touched.
/// </summary>
public sealed class StoredCase
{
    public Guid CaseId { get; init; }
    public Guid AlertId { get; init; }
    public StoredAlert Alert { get; init; } = null!;
    public CaseStatus Status { get; set; }
    public DateTimeOffset OpenedAt { get; init; }

    /// <summary>When its one Investigation began; the daily limit counts these, so the count survives a restart.</summary>
    public DateTimeOffset? InvestigationStartedAt { get; set; }

    /// <summary>Left Open because Investigations were switched off or the day's limit was spent.</summary>
    public bool NotInvestigated { get; set; }

    public bool ReviewFlagged { get; set; }
    public bool InvestigationFailed { get; set; }

    /// <summary>The Finding as the LLM wrote it, so still in Pseudonyms; only the analyst-facing read unmasks it.</summary>
    public string? Finding { get; set; }

    /// <summary>Pseudonym to real identifier, for this Case's Investigation only.</summary>
    public Dictionary<string, string>? PseudonymMap { get; set; }

    /// <summary>Every Evidence Ref the Investigation issued, with the EventIds it stands for, so cited refs can be resolved.</summary>
    public Dictionary<string, List<Guid>>? EvidenceRefs { get; set; }

    public string? PromptVersion { get; set; }

    /// <summary>
    /// The Investigation span as a W3C traceparent, so the decision's own trace can link back to it however long the
    /// approval waits. Null when nothing was tracing the Investigation.
    /// </summary>
    public string? InvestigationTrace { get; set; }

    /// <summary>Set once, by Act; null until the analyst decides.</summary>
    public StoredAnalystDecision? Decision { get; set; }

    /// <summary>Set by Act unless the decision's action was NoAction.</summary>
    public StoredIntervention? Intervention { get; set; }
}
