using Vigil.Api.Events;
using Vigil.Domain.Fraud;

namespace Vigil.Api.Fraud;

public sealed record ConfirmedFraudRequest(DateTimeOffset DecidedAt);

public static class ConfirmedFraudEndpoints
{
    public static IEndpointRouteBuilder MapConfirmedFraudEndpoints(this IEndpointRouteBuilder app)
    {
        // Seeding only: Stage 3's analyst decisions will reach the same record through the Case workflow.
        app.MapPost("/accounts/{accountId}/confirmed-fraud",
                async (string accountId, ConfirmedFraudRequest request, Ingestion ingestion, CancellationToken ct) =>
                    TypedResults.Ok(await ingestion.RecordConfirmedFraudAsync(
                        new ConfirmedFraud(accountId, request.DecidedAt, ConfirmedFraudSource.Seed), ct)))
            .WithSummary("Seed an Account as Confirmed Fraud and re-score the Accounts linked to it. Idempotent: the first decision is kept and returned.");

        return app;
    }
}
