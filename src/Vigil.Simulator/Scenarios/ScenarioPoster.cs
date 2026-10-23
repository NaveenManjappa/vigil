using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Vigil.Simulator.Scenarios;

/// <summary>Posts a Scenario to the API: its events, then its Confirmed Fraud decision if it has one. Shared with the API tests.</summary>
public static class ScenarioPoster
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    /// <returns>The API's ingestion result, as text for the log.</returns>
    public static async Task<string> PostAsync(HttpClient http, Scenario scenario, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync("/events", scenario.Events(), Json, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        response.EnsureSuccessStatusCode();

        // After the events, so the Account and its links are in the graph when the decision re-checks its Neighbours.
        if (scenario.ConfirmedFraud is { } fraud)
        {
            var seeded = await http.PostAsJsonAsync($"/accounts/{fraud.AccountId}/confirmed-fraud", new { fraud.DecidedAt }, Json, ct);
            seeded.EnsureSuccessStatusCode();
        }

        return body;
    }
}
