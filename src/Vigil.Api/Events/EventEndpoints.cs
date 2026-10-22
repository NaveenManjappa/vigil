using Vigil.Infrastructure.Events;

namespace Vigil.Api.Events;

public static class EventEndpoints
{
    public static IEndpointRouteBuilder MapEventEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/events", IngestAsync)
            .WithSummary("Ingest a batch of portal events (a single event is a batch of one).");

        app.MapGet("/accounts/{accountId}/timeline", async (string accountId, EventStore store, CancellationToken ct) =>
                TypedResults.Ok(await store.GetTimelineAsync(accountId, ct)))
            .WithSummary("Events for an account, ordered by when they occurred.");

        return app;
    }

    private static async Task<IResult> IngestAsync(PortalEventRequest[] batch, EventStore store, CancellationToken ct)
    {
        // Whole-batch validation: a source either sends a clean batch or fixes it and resends,
        // so we never have to explain which half of a batch was kept.
        var errors = batch
            .SelectMany((e, i) => e.Validate().Select(err => (Key: $"[{i}].{err.Field}", err.Error)))
            .GroupBy(x => x.Key, x => x.Error)
            .ToDictionary(g => g.Key, g => g.ToArray());

        if (batch.Length == 0) errors["batch"] = ["must contain at least one event"];
        if (errors.Count > 0) return TypedResults.ValidationProblem(errors);

        var result = await store.AppendAsync(batch.Select(e => e.ToDomain()).ToList(), ct);
        return TypedResults.Ok(result);
    }
}
