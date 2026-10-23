using Vigil.Api.Events;
using Vigil.Domain.Fraud;
using Vigil.Infrastructure.Fraud;

namespace Vigil.Api.Fraud;

public sealed record ConfirmedFraudRequest(DateTimeOffset DecidedAt);

public static class ConfirmedFraudEndpoints
{
    public static IEndpointRouteBuilder MapConfirmedFraudEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/accounts/{accountId}/confirmed-fraud",
                async Task<IResult> (string accountId, ConfirmedFraudStore frauds, CancellationToken ct) =>
                    await frauds.FindAsync(accountId, ct) is { } fraud ? TypedResults.Ok(fraud) : TypedResults.NotFound())
            .WithSummary("The Account's Confirmed Fraud, with when and by what source it was decided; 404 if it is not one.");

        // Seeding only: analyst decisions reach the same record through the Case's Act step.
        app.MapPost("/accounts/{accountId}/confirmed-fraud",
                async (string accountId, ConfirmedFraudRequest request, Ingestion ingestion, CancellationToken ct) =>
                    TypedResults.Ok(await ingestion.RecordConfirmedFraudAsync(
                        new ConfirmedFraud(accountId, request.DecidedAt, ConfirmedFraudSource.Seed), ct)))
            .WithSummary("Seed an Account as Confirmed Fraud and re-score the Accounts linked to it. Idempotent: the first decision is kept and returned.");

        return app;
    }
}
