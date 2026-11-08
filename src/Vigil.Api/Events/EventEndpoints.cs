namespace Vigil.Api.Events;

public static class EventEndpoints
{
    public static IEndpointRouteBuilder MapEventEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/events", IngestAsync)
            .WithTags("1. Events")
            .WithSummary("Send one Portal Event. The response says which Rules it triggered, the Alert's new Score and whether a Case opened.");

        return app;
    }

    private static async Task<IResult> IngestAsync(PortalEventRequest request, Ingestion ingestion, CancellationToken ct)
    {
        var errors = request.Validate();
        if (errors.Count > 0) return TypedResults.ValidationProblem(errors);

        return TypedResults.Ok(await ingestion.IngestAsync(request.ToDomain(), ct));
    }
}
