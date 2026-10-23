using Vigil.Agents;
using Vigil.Domain.Cases;
using Vigil.Domain.Detection;
using Vigil.Infrastructure.Audit;
using Vigil.Infrastructure.Cases;

namespace Vigil.Api.Cases;

public sealed record CaseSummary(
    Guid Id,
    Guid AlertId,
    string AccountId,
    CaseStatus Status,
    RiskBand Band,
    int Score,
    DateTimeOffset OpenedAt,
    bool NotInvestigated);

/// <summary>One cited Evidence Ref and the Portal Events behind it, so the analyst can check the claim.</summary>
public sealed record ResolvedEvidenceRef(string Ref, IReadOnlyList<Guid> EventIds);

/// <summary>The Finding as the analyst reads it: real identifiers in the summary, refs resolved to EventIds.</summary>
public sealed record AnalystFinding(
    Verdict Verdict, Confidence Confidence, string Summary, Recommendation Recommendation, IReadOnlyList<ResolvedEvidenceRef> Evidence);

/// <summary>The Analyst Decision on a Case, once made.</summary>
public sealed record DecisionView(DecisionOutcome Outcome, Recommendation Action, string AnalystId, string? Notes, DateTimeOffset DecidedAt);

/// <summary>The Intervention the decision recorded for the portal; Until is set for BlockBankChanges only.</summary>
public sealed record InterventionView(InterventionKind Kind, DateTimeOffset? Until, DateTimeOffset RecordedAt);

public sealed record CaseDetail(
    Guid Id,
    Guid AlertId,
    string AccountId,
    CaseStatus Status,
    RiskBand Band,
    int Score,
    DateTimeOffset OpenedAt,
    bool NotInvestigated,
    bool ReviewFlagged,
    bool InvestigationFailed,
    string? PromptVersion,
    AnalystFinding? Finding,
    DecisionView? Decision,
    InterventionView? Intervention)
{
    public static CaseDetail From(StoredCase c) => new(
        c.CaseId, c.AlertId, c.Alert.AccountId, c.Status, c.Alert.Band, c.Alert.Score, c.OpenedAt,
        c.NotInvestigated, c.ReviewFlagged, c.InvestigationFailed, c.PromptVersion, Unmasked(c),
        c.Decision is { } d
            ? new DecisionView(Enum.Parse<DecisionOutcome>(d.Outcome), Enum.Parse<Recommendation>(d.Action), d.AnalystId, d.Notes, d.DecidedAt)
            : null,
        c.Intervention is { } i ? new InterventionView(i.Kind, i.Until, i.RecordedAt) : null);

    // The only place a Finding is unmasked: it is stored in Pseudonyms and turned back only for the analyst (ADR-0008).
    private static AnalystFinding? Unmasked(StoredCase c)
    {
        if (c.Finding is null) return null;

        var finding = Agents.Finding.FromJson(c.Finding);
        var masker = PseudonymMasker.FromMap(c.PseudonymMap ?? []);
        var refs = c.EvidenceRefs ?? [];
        return new AnalystFinding(
            finding.Verdict, finding.Confidence, masker.Unmask(finding.Summary), finding.Recommendation,
            [.. finding.Evidence.Select(r => new ResolvedEvidenceRef(r, refs.GetValueOrDefault(r) ?? []))]);
    }
}

public static class CaseEndpoints
{
    public static IEndpointRouteBuilder MapCaseEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/cases", async (CaseStatus? status, CaseStore store, CancellationToken ct) =>
                TypedResults.Ok((await store.QueueAsync(status, ct))
                    .Select(c => new CaseSummary(c.CaseId, c.AlertId, c.Alert.AccountId, c.Status, c.Alert.Band, c.Alert.Score, c.OpenedAt, c.NotInvestigated))))
            .WithSummary("The Case queue in priority order (Risk Band, then Score, then time opened), optionally filtered by status.");

        app.MapGet("/cases/{id:guid}", async (Guid id, CaseStore store, CancellationToken ct) =>
                await store.FindWithAlertAsync(id, ct) is { } @case
                    ? Results.Ok(CaseDetail.From(@case))
                    : Results.NotFound())
            .WithSummary("One Case with its unmasked Finding, each cited Evidence Ref resolved to EventIds, and its decision and Intervention once made.");

        app.MapGet("/cases/{id:guid}/audit", async (Guid id, AuditStore audit, CancellationToken ct) =>
                TypedResults.Ok((await audit.ForCaseAsync(id, ct)).Select(AuditEntryView.From)))
            .WithSummary("The Case's Audit Entries in the order they were written.");

        app.MapPost("/cases/{id:guid}/decision", DecideAsync)
            .WithSummary("Records the analyst's decision on a Case awaiting approval, resuming its Investigation so Act runs.");

        return app;
    }

    private static async Task<IResult> DecideAsync(
        Guid id, AnalystDecision decision, CaseStore store, InvestigationWorkflow workflow, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(decision.AnalystId))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["analystId"] = ["A decision must name its analyst."] });

        var @case = await store.FindWithAlertAsync(id, ct);
        if (@case is null) return Results.NotFound();
        if (@case.Status != CaseStatus.AwaitingApproval)
            return Results.Problem($"Case {id} is {@case.Status}; a decision is accepted only in AwaitingApproval.", statusCode: StatusCodes.Status409Conflict);

        try
        {
            await workflow.DecideAsync(id, decision, ct);
        }
        catch (Exception e) when (Causes<DecisionAlreadyRecordedException>(e))
        {
            // Both decisions saw AwaitingApproval; the database let the other one in first.
            return Results.Problem($"Case {id} was decided by another request; a Case takes one decision.", statusCode: StatusCodes.Status409Conflict);
        }

        // Read again: Act may have reloaded the Case under a cleared change tracker, leaving the instance above stale.
        return Results.Ok(CaseDetail.From((await store.FindWithAlertAsync(id, ct))!));
    }

    // The workflow wraps what Act throws, so the cause is looked for down the chain.
    private static bool Causes<T>(Exception? e) where T : Exception
    {
        for (; e is not null; e = e.InnerException)
            if (e is T) return true;
        return false;
    }
}
