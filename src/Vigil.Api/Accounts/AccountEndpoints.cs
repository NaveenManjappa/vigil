using Microsoft.EntityFrameworkCore;
using Vigil.Api.Data;
using Vigil.Domain.Detection;
using Vigil.Domain.Events;
using Vigil.Domain.Graph;
using Vigil.Graph;

namespace Vigil.Api.Accounts;

/// <summary>One event in the account view, with the number the Investigator cites it by.</summary>
public sealed record NumberedEvent(int Number, PortalEvent Event);

/// <summary>Everything Vigil knows about one Account, in one response.</summary>
public sealed record AccountView(
    string AccountId,
    bool IsConfirmedFraud,
    IReadOnlyList<NumberedEvent> Events,
    IReadOnlyList<Alert> Alerts,
    IReadOnlyList<Case> Cases,
    IReadOnlyList<LinkedIdentifier> LinkedIdentifiers);

public static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/accounts/{accountId}", GetAsync)
            .WithTags("2. Accounts")
            .WithSummary("Everything about one Account: its numbered events, its Alerts, its Cases and the Accounts it shares identifiers with.");

        app.MapPost("/accounts/{accountId}/confirm-fraud", async (string accountId, GraphProjector graph, CancellationToken ct) =>
            {
                await graph.MarkConfirmedFraudAsync(accountId, ct);
                return TypedResults.Ok(new { Message = $"{accountId} is now Confirmed Fraud. Rule G2 now hits any Account that shares its device, email, phone or bank account." });
            })
            .WithTags("2. Accounts")
            .WithSummary("Mark an Account as known fraud, without a Case. The walkthrough uses it to set up Derek.");

        return app;
    }

    private static async Task<AccountView> GetAsync(
        string accountId, AccountData accounts, VigilDbContext db, GraphNeighbourhood graph, CancellationToken ct)
    {
        var timeline = await accounts.GetTimelineAsync(accountId, ct);
        var alerts = await db.Alerts.AsNoTracking().Where(a => a.AccountId == accountId).ToListAsync(ct);
        var cases = await db.Cases.AsNoTracking().Include(c => c.Alert).Where(c => c.AccountId == accountId)
            .OrderBy(c => c.OpenedAt).ToListAsync(ct);
        var neighbourhood = await graph.ForAccountAsync(accountId, ct);

        return new AccountView(
            accountId,
            neighbourhood.IsConfirmedFraud,
            [.. timeline.Select((e, i) => new NumberedEvent(i + 1, e))],
            [.. alerts.OrderByDescending(a => a.IsOpen)],
            cases,
            neighbourhood.Identifiers);
    }
}
