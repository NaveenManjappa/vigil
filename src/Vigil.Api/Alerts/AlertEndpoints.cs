using Vigil.Domain.Detection;
using Vigil.Infrastructure.Alerts;

namespace Vigil.Api.Alerts;

public sealed record AlertSummary(Guid Id, string AccountId, int Score, RiskBand Band, string RulesVersion, DateTimeOffset CreatedAt);

public sealed record AlertDetail(
    Guid Id,
    string AccountId,
    int Score,
    RiskBand Band,
    string RulesVersion,
    DateTimeOffset CreatedAt,
    IReadOnlyList<RuleHit> Hits);

public static class AlertEndpoints
{
    public static IEndpointRouteBuilder MapAlertEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/alerts", async (RiskBand? band, AlertStore store, CancellationToken ct) =>
                TypedResults.Ok((await store.ListAsync(band, ct))
                    .Select(a => new AlertSummary(a.AlertId, a.AccountId, a.Score, a.Band, a.RulesVersion, a.CreatedAt))))
            .WithSummary("Alerts, riskiest first, optionally filtered by risk band.");

        app.MapGet("/alerts/{id:guid}", async Task<IResult> (Guid id, AlertStore store, CancellationToken ct) =>
                await store.FindAsync(id, ct) is { } a
                    ? TypedResults.Ok(new AlertDetail(a.AlertId, a.AccountId, a.Score, a.Band, a.RulesVersion, a.CreatedAt,
                        a.Hits.Select(h => h.ToDomain()).ToList()))
                    : TypedResults.NotFound())
            .WithSummary("One alert with its rule hits and evidence event IDs.");

        app.MapGet("/rules", (RuleSet rules) => TypedResults.Ok(rules))
            .WithSummary("The active rule set and its version.");

        return app;
    }
}
