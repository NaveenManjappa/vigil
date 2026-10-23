extern alias simulator;

using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Neo4j.Driver;
using simulator::Vigil.Simulator.Scenarios;
using Vigil.Api.Graph;
using Vigil.Domain.Detection;
using Vigil.Domain.Events;
using Vigil.Graph;

namespace Vigil.Api.Tests;

/// <summary>The Projection is disposable: wiped and rebuilt from Postgres, it gives every Account the same graph Rule Hits.</summary>
public sealed class GraphRebuildTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateTimeOffset Monday = new(2026, 9, 21, 9, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private readonly HttpClient _client = factory.CreateClient();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static PortalEvent BankChange(string account, DateTimeOffset at, string bankAccount) =>
        new(Guid.NewGuid(), account, $"HOLDER-{account}", EventType.BankDetailsChanged, at, $"D-{account}", "198.51.100.1", "GB", "web",
            new Dictionary<string, string> { ["NewBankAccountHash"] = bankAccount });

    // The demo gives G2 (Confirmed Fraud ACC-PRIYA) and G3 (D-77); two Holders paying out to one bank account give G1.
    private async Task<List<string>> PostDemoAsync()
    {
        var scenarios = Scenario.LoadAll(Path.Combine(AppContext.BaseDirectory, "scenarios"));
        foreach (var scenario in scenarios) await ScenarioPoster.PostAsync(_client, scenario, Ct);

        PortalEvent[] shared = [BankChange("ACC-MULE-1", Monday, "BA-REBUILD"), BankChange("ACC-MULE-2", Monday.AddDays(1), "BA-REBUILD")];
        (await _client.PostAsJsonAsync("/events", shared, Json, Ct)).EnsureSuccessStatusCode();

        return [.. scenarios.SelectMany(s => s.Events()).Concat(shared).Select(e => e.AccountId).Distinct()];
    }

    private async Task<Dictionary<string, IReadOnlyList<RuleHit>>> GraphHitsAsync(IEnumerable<string> accounts)
    {
        var rules = factory.Services.GetRequiredService<GraphRules>();
        var ruleSet = factory.Services.GetRequiredService<RuleSet>();
        var hits = new Dictionary<string, IReadOnlyList<RuleHit>>();
        foreach (var account in accounts) hits[account] = await rules.HitsForAsync(account, ruleSet, Ct);
        return hits;
    }

    private async Task RebuildAsync()
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<GraphRebuild>().RunAsync(Ct);
    }

    private Task WriteGraphAsync(string cypher) =>
        factory.Services.GetRequiredService<IDriver>().ExecutableQuery(cypher).ExecuteAsync(Ct);

    [Fact]
    public async Task After_Neo4j_data_is_lost_a_rebuild_brings_back_every_Accounts_graph_Rule_Hits()
    {
        var accounts = await PostDemoAsync();
        var before = await GraphHitsAsync(accounts);
        Assert.Equal(["G1", "G2", "G3"], before.Values.SelectMany(h => h).Select(h => h.RuleId).Distinct().Order());

        await WriteGraphAsync("MATCH (n) DETACH DELETE n");
        Assert.All((await GraphHitsAsync(accounts)).Values, Assert.Empty);

        await RebuildAsync();

        Assert.Equivalent(before, await GraphHitsAsync(accounts), strict: true);
    }

    [Fact]
    public async Task A_rebuild_removes_links_that_are_not_in_Postgres()
    {
        var accounts = await PostDemoAsync();
        var before = await GraphHitsAsync(accounts);

        // As if Neo4j committed a batch whose Postgres commit then failed (ADR-0015).
        await factory.Services.GetRequiredService<GraphProjector>()
            .ProjectAsync([BankChange("ACC-STRAY", Monday.AddDays(2), "BA-REBUILD")], Ct);
        Assert.NotEmpty((await GraphHitsAsync(["ACC-STRAY"]))["ACC-STRAY"]);

        await RebuildAsync();

        Assert.Empty((await GraphHitsAsync(["ACC-STRAY"]))["ACC-STRAY"]);
        Assert.Equivalent(before, await GraphHitsAsync(accounts), strict: true);
    }
}
