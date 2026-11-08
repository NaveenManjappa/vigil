using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Vigil.Agents;
using Vigil.Api.Data;
using Vigil.Domain.Cases;
using Vigil.Graph;

namespace Vigil.Api.Cases;

/// <summary>The body of <c>POST /cases/{id}/decision</c>. The action may differ from the Finding's recommendation.</summary>
public sealed record AnalystDecision(DecisionOutcome Outcome, Recommendation Action, string AnalystId, string? Notes);

/// <summary>How long a BlockBankChanges Intervention lasts, from the <c>Intervention</c> config section.</summary>
public sealed class InterventionOptions
{
    public int BlockBankChangesDays { get; init; } = 7;
}

public static class CaseEndpoints
{
    public static IEndpointRouteBuilder MapCaseEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/cases", async (CaseStatus? status, VigilDbContext db, CancellationToken ct) =>
            {
                var cases = await db.Cases.AsNoTracking().Include(c => c.Alert)
                    .Where(c => status == null || c.Status == status).ToListAsync(ct);
                return cases.OrderByDescending(c => c.Alert.Band).ThenByDescending(c => c.Alert.Score).ThenBy(c => c.OpenedAt);
            })
            .WithTags("3. Cases")
            .WithSummary("The Case queue, riskiest first, optionally filtered by status.");

        app.MapGet("/cases/{id:guid}", async Task<IResult> (Guid id, VigilDbContext db, CancellationToken ct) =>
                await FindAsync(db, id, ct) is { } @case ? TypedResults.Ok(@case) : TypedResults.NotFound())
            .WithTags("3. Cases")
            .WithSummary("One Case with its Alert, its Finding once investigated, and its decision once made.");

        app.MapPost("/cases/{id:guid}/investigate", InvestigateAsync)
            .WithTags("3. Cases")
            .WithSummary("Run the Investigator agent on an Open Case. It returns the Case with its Finding and moves it to AwaitingDecision.");

        app.MapPost("/cases/{id:guid}/decision", DecideAsync)
            .WithTags("3. Cases")
            .WithSummary("Record the analyst's decision on a Case that is AwaitingDecision. This closes the Case.");

        return app;
    }

    private static async Task<IResult> InvestigateAsync(
        Guid id, VigilDbContext db, Investigator investigator, TimeProvider clock, ILogger<Investigator> logger, CancellationToken ct)
    {
        var @case = await FindAsync(db, id, ct);
        if (@case is null) return TypedResults.NotFound();
        if (@case.Status != CaseStatus.Open)
            return TypedResults.Problem($"Case {id} is {@case.Status}; only an Open Case is investigated.", statusCode: StatusCodes.Status409Conflict);

        InvestigationResult result;
        try
        {
            result = await investigator.InvestigateAsync(@case.Alert, ct);
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            // The Case stays Open, so calling investigate again retries it.
            logger.LogError(e, "Investigation of Case {CaseId} failed", id);
            return TypedResults.Problem($"The Investigator failed: {e.Message}. The Case is still Open; try again.", statusCode: StatusCodes.Status502BadGateway);
        }

        @case.Finding = result.Finding;
        @case.Model = result.Model;
        @case.InputTokens = result.InputTokens;
        @case.OutputTokens = result.OutputTokens;
        @case.InvestigatedAt = clock.GetUtcNow();
        @case.Status = CaseStatus.AwaitingDecision;
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(@case);
    }

    private static async Task<IResult> DecideAsync(
        Guid id, AnalystDecision decision, VigilDbContext db, GraphProjector graph,
        IOptions<InterventionOptions> options, TimeProvider clock, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(decision.AnalystId))
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["analystId"] = ["A decision must name its analyst."] });

        var @case = await FindAsync(db, id, ct);
        if (@case is null) return TypedResults.NotFound();
        if (@case.Status != CaseStatus.AwaitingDecision)
            return TypedResults.Problem($"Case {id} is {@case.Status}; a decision is accepted only in AwaitingDecision.", statusCode: StatusCodes.Status409Conflict);

        var now = clock.GetUtcNow();
        @case.Outcome = decision.Outcome;
        @case.Action = decision.Action;
        @case.AnalystId = decision.AnalystId;
        @case.Notes = decision.Notes;
        @case.DecidedAt = now;
        (@case.Intervention, @case.InterventionUntil) = decision.Action switch
        {
            Recommendation.BlockBankChanges => (InterventionKind.BlockBankChanges, now.AddDays(options.Value.BlockBankChangesDays)),
            Recommendation.ContactHolder => (InterventionKind.ContactHolder, (DateTimeOffset?)null),
            _ => ((InterventionKind?)null, (DateTimeOffset?)null),
        };
        @case.Status = CaseStatus.Closed;
        // Closing the Case closes its Alert, so the Account's next Rule Hit starts a fresh Alert.
        @case.Alert.IsOpen = false;

        // G2 then hits any Account that later shares this one's device, email, phone or bank account.
        if (decision.Outcome == DecisionOutcome.ConfirmedFraud)
            await graph.MarkConfirmedFraudAsync(@case.AccountId, ct);

        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(@case);
    }

    private static Task<Case?> FindAsync(VigilDbContext db, Guid id, CancellationToken ct) =>
        db.Cases.Include(c => c.Alert).SingleOrDefaultAsync(c => c.Id == id, ct);
}
