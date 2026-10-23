using Vigil.Domain.Cases;
using Vigil.Domain.Detection;
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

public static class CaseEndpoints
{
    public static IEndpointRouteBuilder MapCaseEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/cases", async (CaseStatus? status, CaseStore store, CancellationToken ct) =>
                TypedResults.Ok((await store.QueueAsync(status, ct))
                    .Select(c => new CaseSummary(c.CaseId, c.AlertId, c.Alert.AccountId, c.Status, c.Alert.Band, c.Alert.Score, c.OpenedAt, c.NotInvestigated))))
            .WithSummary("The Case queue in priority order (Risk Band, then Score, then time opened), optionally filtered by status.");

        return app;
    }
}
