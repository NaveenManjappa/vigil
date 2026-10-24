using System.Net.Http.Json;
using Vigil.Domain.Datasets;
using Vigil.Domain.Events;

namespace Vigil.Simulator.StandIn;

/// <summary>Posts a Dataset's events to the API, without their Labels, so detection sees what a live portal would send.</summary>
public static class DatasetPoster
{
    // Small enough that one batch's detection and graph projection finish inside the resilience handler's attempt timeout.
    public const int DefaultBatchSize = 200;

    /// <summary>
    /// Every Account's events in one OccurredAt order, so a shared device or IP is in the graph before a later Account
    /// links to it, as it would be live. Ties keep Dataset order, so every run posts the same sequence.
    /// </summary>
    public static IEnumerable<IReadOnlyList<PortalEvent>> Batches(IEnumerable<LabelledAccount> accounts, int batchSize = DefaultBatchSize) =>
        accounts.SelectMany(a => a.Events).OrderBy(e => e.OccurredAt).Chunk(batchSize);

    /// <returns>The number of events posted.</returns>
    public static async Task<int> PostAsync(HttpClient http, IEnumerable<LabelledAccount> accounts, CancellationToken ct = default)
    {
        var posted = 0;
        foreach (var batch in Batches(accounts))
        {
            // Ingestion is idempotent on EventId, so a re-run after a failure resends safely.
            var response = await http.PostAsJsonAsync("/events", batch, Dataset.JsonOptions, ct);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"POST /events failed with {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(ct)}");
            posted += batch.Count;
        }
        return posted;
    }
}
