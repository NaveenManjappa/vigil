extern alias simulator;

using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using simulator::Vigil.Simulator.Scenarios;
using Vigil.Api.Alerts;
using Vigil.Domain.Detection;
using Vigil.Domain.Events;

namespace Vigil.Api.Tests;

/// <summary>The PROJECT.md Section 3 demo, run through the API the way the simulator runs it.</summary>
public sealed class ScenarioTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private readonly HttpClient _client = factory.CreateClient();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task PostAsync(Scenario scenario) =>
        (await _client.PostAsJsonAsync("/events", scenario.Events(), Json, Ct)).EnsureSuccessStatusCode();

    private async Task<List<AlertSummary>> AlertsForAsync(string accountId) =>
        (await _client.GetFromJsonAsync<List<AlertSummary>>("/alerts", Json, Ct))!.Where(a => a.AccountId == accountId).ToList();

    [Fact]
    public async Task Margaret_reaches_High_at_the_email_change_before_any_bank_change()
    {
        var scenario = new MargaretAto();
        var events = scenario.Events();
        var reset = events.Single(e => e.Type == EventType.PasswordReset);
        var mfa = events.Single(e => e.Type == EventType.MfaEnrolled);
        var email = events.Single(e => e.Type == EventType.EmailChanged);
        var bank = events.Single(e => e.Type == EventType.BankDetailsChanged);

        await PostAsync(scenario);

        var summary = Assert.Single(await AlertsForAsync("ACC-MARGARET"));
        var alert = (await _client.GetFromJsonAsync<AlertDetail>($"/alerts/{summary.Id}", Json, Ct))!;

        var r0 = Assert.Single(alert.Hits, h => h.RuleId == "R0");
        var r1 = Assert.Single(alert.Hits, h => h.RuleId == "R1");
        var r2 = Assert.Single(alert.Hits, h => h.RuleId == "R2");
        Assert.Equal([reset.EventId], r0.EvidenceEventIds);
        Assert.Equal([reset.EventId, mfa.EventId], r1.EvidenceEventIds);
        Assert.Equal([reset.EventId, email.EventId], r2.EvidenceEventIds);

        // The hits triggered by the email change add up to 60, the High threshold (PROJECT.md Section 7.3),
        // and the bank change comes the next day.
        Assert.Equal(email.OccurredAt, r2.TriggeredAt);
        Assert.Equal(60, alert.Hits.Where(h => h.TriggeredAt <= email.OccurredAt).Sum(h => h.Points));
        Assert.True(email.OccurredAt < bank.OccurredAt);
    }

    [Fact]
    public async Task Mr_Patel_never_reaches_Medium()
    {
        await PostAsync(new MrPatelHouseMove());

        // Usual laptop, usual country, no reset: nothing for any Rule to match, so not even a Low Alert.
        Assert.Empty(await AlertsForAsync("ACC-PATEL"));
    }
}
