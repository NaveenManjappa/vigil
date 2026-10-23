using Vigil.Graph;
using Vigil.Infrastructure.Events;
using Vigil.Infrastructure.Fraud;

namespace Vigil.Api.Graph;

/// <summary>
/// The rebuild-graph command (ADR-0015). It lives here because it joins the two stores: Vigil.Graph knows nothing of
/// Postgres. Alerts are not re-scored: they are append-only (ADR-0013), and the rebuilt graph gives the same hits.
/// </summary>
public sealed class GraphRebuild(EventStore events, ConfirmedFraudStore frauds, GraphProjector graph, ILogger<GraphRebuild> logger)
{
    public async Task RunAsync(CancellationToken ct)
    {
        var confirmed = await frauds.AllAsync(ct);
        var projected = await graph.RebuildAsync(events.AllAsync(GraphProjector.RebuildBatchSize, ct), confirmed, ct);
        logger.LogInformation("Rebuilt the graph from {Events} Portal Events and {ConfirmedFrauds} Confirmed Frauds", projected, confirmed.Count);
    }
}
