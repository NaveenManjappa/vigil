using System.Text.Json;

namespace Vigil.Agents;

/// <summary>One agent call's tokens and model, kept for the Audit Entry.</summary>
/// <param name="Model">Last, and optional, so checkpoints written before it was added still read.</param>
public sealed record AgentUsage(string AgentId, string PromptVersion, long InputTokens, long OutputTokens, string? Model = null);

/// <summary>
/// What an Investigation hands to Await Approval. Every path produces one, so a Case reaches the analyst even when
/// the agents failed.
/// </summary>
/// <param name="Finding">The last Finding written; null when the Investigation failed.</param>
/// <param name="ReviewFlagged">The Finding was still rejected after its one retry.</param>
/// <param name="RejectionReasons">Why the kept Finding was last rejected; empty when it was approved.</param>
/// <param name="Usage">Every agent call that returned, in order, including those on a failed run, because they were billed.</param>
/// <param name="Failure">Why the Investigation failed, for the logs; null when it did not.</param>
public sealed record InvestigationOutcome(
    Finding? Finding,
    bool ReviewFlagged,
    IReadOnlyList<string> RejectionReasons,
    IReadOnlyList<AgentUsage> Usage,
    string? Failure)
{
    public bool InvestigationFailed => Finding is null;
}

/// <summary>
/// Investigate, Validate, Review, with one retry (ADR-0006). Validation failures and Reviewer rejections are treated
/// alike: the Investigator gets the reasons and tries once more; a second rejection keeps the Finding with the
/// Review Flag, unless it is incomplete. An LLM failure or an incomplete last Finding ends the Investigation with no Finding.
/// </summary>
public sealed class InvestigationLoop(Investigator investigator, Reviewer reviewer)
{
    public const int MaxAttempts = 2;

    private const string NotAFinding = "Your reply was not a Finding: reply with one JSON object in the required shape and nothing else.";

    public async Task<InvestigationOutcome> RunAsync(Enrichment enrichment, CancellationToken ct)
    {
        var usage = new List<AgentUsage>();
        IReadOnlyList<string> reasons = [];
        Finding? finding = null;

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            InvestigatorResult investigated;
            try
            {
                investigated = await investigator.InvestigateAsync(enrichment, reasons, ct);
            }
            catch (JsonException) when (attempt < MaxAttempts)
            {
                // Unparseable output is often a one-off formatting slip, so it gets the retry a rejection would.
                reasons = [NotAFinding];
                continue;
            }
            catch (Exception e) when (!ct.IsCancellationRequested)
            {
                return Failed(usage, e);
            }
            usage.Add(new(Investigator.AgentId, Investigator.PromptVersion, investigated.InputTokens, investigated.OutputTokens, investigated.Model));
            finding = investigated.Finding;

            reasons = FindingValidation.Check(finding, enrichment.Evidence.Refs);
            if (reasons.Count > 0)
                continue;

            ReviewerResult reviewed;
            try
            {
                reviewed = await reviewer.ReviewAsync(finding, enrichment.Evidence, ct);
            }
            catch (Exception e) when (!ct.IsCancellationRequested)
            {
                return Failed(usage, e);
            }
            usage.Add(new(Reviewer.AgentId, Reviewer.PromptVersion, reviewed.InputTokens, reviewed.OutputTokens, reviewed.Model));
            if (reviewed.Review.Approved)
                return new(finding, ReviewFlagged: false, [], usage, Failure: null);

            // A rejection with no reasons would give the retry nothing to fix.
            reasons = reviewed.Review.Reasons.Count > 0 ? reviewed.Review.Reasons : ["The Reviewer rejected the Finding without a reason."];
        }

        // Non-null here: the last attempt either returned from a catch or wrote a Finding.
        if (!FindingValidation.IsComplete(finding!))
            return new(null, ReviewFlagged: false, [], usage, "The last Finding was incomplete: " + string.Join(" ", reasons));
        return new(finding!, ReviewFlagged: true, reasons, usage, Failure: null);
    }

    // Timeouts surface as OperationCanceledException with our token untouched, so they fail the Investigation like
    // any LLM error. Only a cancelled token, the host stopping, escapes: the Case is then re-queued on startup.
    private static InvestigationOutcome Failed(List<AgentUsage> usage, Exception e) =>
        new(null, ReviewFlagged: false, [], usage, $"{e.GetType().Name}: {e.Message}");
}
