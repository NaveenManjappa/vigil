namespace Vigil.Agents;

/// <summary>
/// The Validate step: the checks code can make on a Finding before any Reviewer call. A failure is a rejection
/// like the Reviewer's, so a hallucinated ref or an overlong summary never costs a Reviewer call (ADR-0006).
/// </summary>
public static class FindingValidation
{
    public const int MaxSummaryWords = 120;

    /// <summary>Why the Finding is rejected; empty when it passes.</summary>
    public static IReadOnlyList<string> Check(Finding finding, EvidenceRefTable refs)
    {
        var reasons = new List<string>();

        // The JSON converter accepts numbers for enums, so a value outside the contract can still parse.
        if (!Enum.IsDefined(finding.Verdict))
            reasons.Add($"The verdict must be one of {string.Join(", ", Enum.GetNames<Verdict>())}.");
        if (!Enum.IsDefined(finding.Confidence))
            reasons.Add($"The confidence must be one of {string.Join(", ", Enum.GetNames<Confidence>())}.");
        if (!Enum.IsDefined(finding.Recommendation))
            reasons.Add($"The recommendation must be one of {string.Join(", ", Enum.GetNames<Recommendation>())}.");

        // A missing JSON property binds to null, not to a parse error.
        var words = finding.Summary?.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length ?? 0;
        if (words == 0)
            reasons.Add("The summary is missing.");
        else if (words > MaxSummaryWords)
            reasons.Add($"The summary is {words} words; it must be at most {MaxSummaryWords}.");

        if (finding.Evidence is not { Count: > 0 })
            reasons.Add("The Finding cites no evidence; cite the Evidence Refs the summary relies on.");
        else if (refs.FindUnknown(finding.Evidence) is { Count: > 0 } unknown)
            reasons.Add($"These Evidence Refs were never given to you: {string.Join(", ", unknown)}. Cite only refs you were shown.");

        return reasons;
    }
}
