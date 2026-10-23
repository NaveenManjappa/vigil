extern alias simulator;

using Microsoft.Extensions.DependencyInjection;
using simulator::Vigil.Simulator.Scenarios;
using Vigil.Agents;
using Vigil.Domain.Detection;
using Vigil.Infrastructure.Alerts;

namespace Vigil.Api.Tests;

/// <summary>
/// Margaret's Case run through the API's real Investigation source and the Fake provider's scripted Investigator: the
/// free demo, end to end below the workflow.
/// </summary>
public sealed class InvestigatorDemoTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly HttpClient _client = factory.CreateClient();

    private async Task<(IServiceScope Scope, Alert Alert)> MargaretAsync()
    {
        foreach (var scenario in Scenario.LoadAll(Path.Combine(AppContext.BaseDirectory, "scenarios")))
            await ScenarioPoster.PostAsync(_client, scenario, Ct);

        var scope = factory.Services.CreateScope();
        var alerts = scope.ServiceProvider.GetRequiredService<AlertStore>();
        var summary = Assert.Single(await alerts.ListAsync(band: null, Ct), a => a.AccountId == "ACC-MARGARET");
        return (scope, (await alerts.FindAsync(summary.AlertId, Ct))!.ToDomain());
    }

    [Fact]
    public async Task The_fake_Investigator_queries_the_graph_and_cites_Enrichs_refs_for_Margaret()
    {
        var (scope, alert) = await MargaretAsync();
        using var _ = scope;
        var source = scope.ServiceProvider.GetRequiredService<IInvestigationSource>();

        var enrichment = await Enrichment.CreateAsync(alert, source, Ct);
        var result = await scope.ServiceProvider.GetRequiredService<Investigator>().InvestigateAsync(enrichment, [], Ct);

        Assert.Equal(Verdict.LikelyAccountTakeover, result.Finding.Verdict);
        Assert.Equal(Recommendation.BlockBankChanges, result.Finding.Recommendation);
        Assert.NotEmpty(result.Finding.Evidence);
        Assert.Empty(enrichment.Evidence.Refs.FindUnknown(result.Finding.Evidence));
        // The one tool call adds the graph facts, and D-77's fact is among the cited evidence.
        Assert.Contains(result.Finding.Evidence, r => r.StartsWith("G-"));
        Assert.DoesNotContain("D-77", result.Finding.Summary);
    }

    [Fact]
    public async Task The_fake_Reviewer_approves_the_fake_Investigators_Finding_for_Margaret()
    {
        var (scope, alert) = await MargaretAsync();
        using var _ = scope;
        var source = scope.ServiceProvider.GetRequiredService<IInvestigationSource>();

        var enrichment = await Enrichment.CreateAsync(alert, source, Ct);
        var outcome = await scope.ServiceProvider.GetRequiredService<InvestigationLoop>().RunAsync(enrichment, Ct);

        Assert.False(outcome.InvestigationFailed);
        Assert.False(outcome.ReviewFlag);
        Assert.Equal([Investigator.AgentId, Reviewer.AgentId], outcome.Usage.Select(u => u.AgentId));
    }

    [Fact]
    public async Task Margarets_baseline_is_her_laptop_in_GB_once_a_week_and_her_neighbourhood_has_D77()
    {
        var (scope, alert) = await MargaretAsync();
        using var _ = scope;
        var source = scope.ServiceProvider.GetRequiredService<IInvestigationSource>();
        var enrichment = await Enrichment.CreateAsync(alert, source, Ct);

        var baseline = await source.GetHolderBaselineAsync("ACC-MARGARET", enrichment.FlaggedFrom, Ct);
        var neighbourhood = await source.GetNeighbourhoodAsync("ACC-MARGARET", Ct);

        Assert.Equal(["GB"], baseline.UsualCountries);
        Assert.Equal(["D-MARGARET-LAPTOP"], baseline.UsualDevices);
        Assert.Equal(1.0, baseline.LoginsPerWeek);
        var d77 = Assert.Single(neighbourhood.Identifiers, i => i.Value == "D-77");
        Assert.Equal(["ACC-DEREK", "ACC-LENA", "ACC-PRIYA"], d77.OtherAccounts.Select(a => a.AccountId));
        Assert.Equal(["ACC-PRIYA"], d77.OtherAccounts.Where(a => a.IsConfirmedFraud).Select(a => a.AccountId));
    }

    [Fact]
    public async Task The_timeline_counts_days_back_from_the_Accounts_latest_event()
    {
        var (scope, _) = await MargaretAsync();
        using var __ = scope;
        var source = scope.ServiceProvider.GetRequiredService<IInvestigationSource>();
        var margaret = Scenario.Load(Path.Combine(AppContext.BaseDirectory, "scenarios", "margaret-ato.json"));

        var timeline = await source.GetTimelineAsync("ACC-MARGARET", days: 2, Ct);

        // Weekly baseline logins are all more than two days before the bank change, so only the takeover remains.
        Assert.Equal(margaret.Steps.Select(s => s.Event.EventId), timeline.Select(e => e.EventId));
    }
}
