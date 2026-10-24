using System.Net;
using System.Net.Http.Json;
using Vigil.Domain.Datasets;

namespace Vigil.Simulator.StandIn;

/// <param name="Conflicts">Cases another analyst decided between the listing and the Stand-in's decision.</param>
public sealed record DecideResult(int Decided, int Conflicts, int NotReady, IReadOnlyList<string> Unlabelled);

/// <summary><c>decide --by-label</c>: the Stand-in Analyst decides every Case awaiting approval through the API.</summary>
public static class DecideCommand
{
    public static async Task<DecideResult> DecideAsync(HttpClient http, IReadOnlyDictionary<string, Label> labels, CancellationToken ct = default)
    {
        var cases = await http.GetFromJsonAsync<List<CaseListing>>("/cases", Dataset.JsonOptions, ct) ?? [];
        var plan = StandInAnalyst.Plan(cases, labels);

        int decided = 0, conflicts = 0;
        foreach (var (caseId, decision) in plan.ToDecide)
        {
            // Through the API rather than the database, so Act runs and a ConfirmedFraud records the Confirmed Fraud.
            var response = await http.PostAsJsonAsync($"/cases/{caseId}/decision", decision, Dataset.JsonOptions, ct);
            if (response.StatusCode == HttpStatusCode.Conflict) { conflicts++; continue; }
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"Deciding Case {caseId} failed with {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(ct)}");
            decided++;
        }

        return new DecideResult(decided, conflicts, plan.NotReady, plan.Unlabelled);
    }
}
