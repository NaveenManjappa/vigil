extern alias simulator;

using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Neo4j.Driver;
using simulator::Vigil.Simulator.Scenarios;
using Vigil.Api.Alerts;
using Vigil.Api.Cases;
using Vigil.Domain.Cases;
using Vigil.Domain.Detection;
using Vigil.Domain.Events;

namespace Vigil.Api.Tests;

/// <summary>The PROJECT.md Section 3 demo, run through the API the way the simulator runs it.</summary>
public sealed class ScenarioTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private readonly HttpClient _client = factory.CreateClient();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Scenario Load(string name) => Scenario.Load(Path.Combine(AppContext.BaseDirectory, "scenarios", $"{name}.json"));

    // Every test posts the whole demo, in the simulator's order: posting is idempotent, so the shared database ends up
    // the same whichever test runs first.
    private async Task PostAllAsync()
    {
        foreach (var scenario in Scenario.LoadAll(Path.Combine(AppContext.BaseDirectory, "scenarios")))
            await ScenarioPoster.PostAsync(_client, scenario, Ct);
    }

    private async Task<List<AlertSummary>> AlertsForAsync(string accountId) =>
        (await _client.GetFromJsonAsync<List<AlertSummary>>("/alerts", Json, Ct))!.Where(a => a.AccountId == accountId).ToList();

    private async Task<List<CaseSummary>> CasesForAsync(string accountId) =>
        (await _client.GetFromJsonAsync<List<CaseSummary>>("/cases", Json, Ct))!.Where(c => c.AccountId == accountId).ToList();

    [Fact]
    public async Task Margaret_is_Critical_at_the_email_change_with_D77_evidence_before_any_bank_change()
    {
        var events = Load("margaret-ato").Events();
        var reset = events.Single(e => e.Type == EventType.PasswordReset);
        var mfa = events.Single(e => e.Type == EventType.MfaEnrolled);
        var email = events.Single(e => e.Type == EventType.EmailChanged);
        var bank = events.Single(e => e.Type == EventType.BankDetailsChanged);

        // Each earlier Account's link to D-77 was made by its own first D-77 event, the fraudster's password reset.
        var derekReset = Load("derek-reset").Steps[0].Event;
        var priyaReset = Load("priya-ato").Steps[0].Event;

        await PostAllAsync();

        var summary = Assert.Single(await AlertsForAsync("ACC-MARGARET"));
        var alert = (await _client.GetFromJsonAsync<AlertDetail>($"/alerts/{summary.Id}", Json, Ct))!;

        var r0 = Assert.Single(alert.Hits, h => h.RuleId == "R0");
        var r1 = Assert.Single(alert.Hits, h => h.RuleId == "R1");
        var r2 = Assert.Single(alert.Hits, h => h.RuleId == "R2");
        Assert.Equal([reset.EventId], r0.EvidenceEventIds);
        Assert.Equal([reset.EventId, mfa.EventId], r1.EvidenceEventIds);
        Assert.Equal([reset.EventId, email.EventId], r2.EvidenceEventIds);

        // D-77 was already linked to Confirmed Fraud Account ACC-PRIYA and to ACC-DEREK and ACC-LENA, so Margaret's
        // first D-77 event, her password reset, completes both graph hits.
        var g2 = Assert.Single(alert.Hits, h => h.RuleId == "G2");
        Assert.Equal([priyaReset.EventId, reset.EventId], g2.EvidenceEventIds);
        Assert.Equal(reset.OccurredAt, g2.TriggeredAt);
        Assert.Contains("D-77", g2.Explanation);
        Assert.Contains("ACC-PRIYA", g2.Explanation);

        // The two earliest other Accounts and Margaret make the smallest group of 3 that reaches the Ring threshold.
        var g3 = Assert.Single(alert.Hits, h => h.RuleId == "G3");
        Assert.Equal([derekReset.EventId, priyaReset.EventId, reset.EventId], g3.EvidenceEventIds);
        Assert.Equal(reset.OccurredAt, g3.TriggeredAt);
        Assert.Contains("D-77", g3.Explanation);

        // By the email change: R0 10 + G2 25 + G3 20 (reset) + R1 30 (MFA) + R2 20 (email) = 105, past Critical at 90
        // (rules.v2.json). The bank change comes the next day and adds R3 30, giving 135.
        Assert.Equal(105, alert.Hits.Where(h => h.TriggeredAt <= email.OccurredAt).Sum(h => h.Points));
        Assert.True(email.OccurredAt < bank.OccurredAt);
        Assert.Equal((135, RiskBand.Critical, "v2"), (alert.Score, alert.Band, alert.RulesVersion));

        var @case = Assert.Single(await CasesForAsync("ACC-MARGARET"));
        Assert.Equal(CaseStatus.Open, @case.Status);
        Assert.Equal(summary.Id, @case.AlertId);
    }

    [Fact]
    public async Task The_graph_links_Margaret_via_D77_to_the_three_earlier_Accounts_one_of_them_Confirmed_Fraud()
    {
        await PostAllAsync();

        // The demo's Neo4j Browser query, returning rows instead of drawing the paths.
        var driver = factory.Services.GetRequiredService<IDriver>();
        var result = await driver.ExecutableQuery("""
            MATCH (:Holder {holderId: 'HOLDER-MARGARET'})-[:OWNS]->(:Account {accountId: 'ACC-MARGARET'})
                  -[:USED_DEVICE]->(:Device {value: 'D-77'})<-[:USED_DEVICE]-(other:Account)
            OPTIONAL MATCH (fraud:Case {outcome: 'ConfirmedFraud'})-[:CONCERNS]->(other)
            RETURN other.accountId AS accountId, fraud IS NOT NULL AS confirmedFraud
            ORDER BY accountId
            """).ExecuteAsync(Ct);

        Assert.Equal(
            [("ACC-DEREK", false), ("ACC-LENA", false), ("ACC-PRIYA", true)],
            result.Result.Select(row => (row["accountId"].As<string>(), row["confirmedFraud"].As<bool>())));
    }

    [Fact]
    public async Task Mr_Patel_never_reaches_Medium()
    {
        await PostAllAsync();

        // Usual laptop, usual country, no reset, and no identifier shared with anyone: not even a Low Alert.
        Assert.Empty(await AlertsForAsync("ACC-PATEL"));
    }
}
