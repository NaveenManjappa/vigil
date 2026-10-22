using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Vigil.Api.Alerts;
using Vigil.Domain.Detection;

namespace Vigil.Api.Tests;

public sealed class AlertTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateTimeOffset Monday = new(2026, 9, 21, 9, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private readonly HttpClient _client = factory.CreateClient();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (Guid Id, object Body) Event(string accountId, string type, DateTimeOffset at)
    {
        var id = Guid.NewGuid();
        return (id, new
        {
            eventId = id,
            accountId,
            holderId = $"HOLDER-{accountId}",
            type,
            occurredAt = at,
            deviceId = "D-77",
            ipAddress = "198.51.100.77",
            country = "RO",
            channel = "web",
        });
    }

    private async Task PostAsync(params object[] batch) =>
        (await _client.PostAsJsonAsync("/events", batch, Ct)).EnsureSuccessStatusCode();

    private async Task<List<AlertSummary>> AlertsAsync(string query = "") =>
        (await _client.GetFromJsonAsync<List<AlertSummary>>($"/alerts{query}", Json, Ct))!;

    [Fact]
    public async Task Reset_then_MFA_raises_an_alert_with_its_evidence()
    {
        const string account = "ACC-R1";
        var reset = Event(account, "PasswordReset", Monday);
        var mfa = Event(account, "MfaEnrolled", Monday.AddMinutes(4));

        await PostAsync(reset.Body, mfa.Body);

        var summary = Assert.Single(await AlertsAsync(), a => a.AccountId == account);
        var alert = (await _client.GetFromJsonAsync<AlertDetail>($"/alerts/{summary.Id}", Json, Ct))!;

        Assert.Equal(30, alert.Score);
        Assert.Equal(RiskBand.Medium, alert.Band);
        Assert.Equal("v1", alert.RulesVersion);
        var hit = Assert.Single(alert.Hits);
        Assert.Equal("R1", hit.RuleId);
        Assert.Equal(30, hit.Points);
        Assert.Equal([reset.Id, mfa.Id], hit.EvidenceEventIds);
        Assert.Equal(Monday.AddMinutes(4), hit.TriggeredAt);
        Assert.Equal("Password reset then MFA enrolment, 4 min apart; window 1h", hit.Explanation);
    }

    [Fact]
    public async Task Alerts_can_be_filtered_by_risk_band()
    {
        const string account = "ACC-BAND";
        await PostAsync(Event(account, "PasswordReset", Monday).Body, Event(account, "MfaEnrolled", Monday.AddMinutes(1)).Body);

        Assert.Contains(await AlertsAsync("?band=Medium"), a => a.AccountId == account);
        Assert.DoesNotContain(await AlertsAsync("?band=High"), a => a.AccountId == account);
        Assert.All(await AlertsAsync("?band=Medium"), a => Assert.Equal(RiskBand.Medium, a.Band));
    }

    [Fact]
    public async Task Events_that_match_no_rule_raise_no_alert()
    {
        const string account = "ACC-QUIET";
        await PostAsync(Event(account, "LoginSucceeded", Monday).Body);

        Assert.DoesNotContain(await AlertsAsync(), a => a.AccountId == account);
    }

    [Fact]
    public async Task Reposting_the_same_events_leaves_the_alert_unchanged()
    {
        const string account = "ACC-REPOST";
        object[] batch = [Event(account, "PasswordReset", Monday).Body, Event(account, "MfaEnrolled", Monday.AddMinutes(4)).Body];

        await PostAsync(batch);
        await PostAsync(batch);

        var summary = Assert.Single(await AlertsAsync(), a => a.AccountId == account);
        var alert = (await _client.GetFromJsonAsync<AlertDetail>($"/alerts/{summary.Id}", Json, Ct))!;
        Assert.Single(alert.Hits);
        Assert.Equal(30, alert.Score);
    }

    [Fact]
    public async Task Concurrent_ingests_for_one_account_produce_one_alert()
    {
        const string account = "ACC-CONCURRENT";
        await PostAsync(Event(account, "PasswordReset", Monday).Body);

        // Each request brings its own MFA event, so all of them complete the pattern and none waits on another's
        // EventId; only the per-Account lock stops each from seeing "no Alert yet" and opening its own.
        await Task.WhenAll(Enumerable.Range(1, 8).Select(i => PostAsync(Event(account, "MfaEnrolled", Monday.AddMinutes(i)).Body)));

        var summary = Assert.Single(await AlertsAsync(), a => a.AccountId == account);
        var alert = (await _client.GetFromJsonAsync<AlertDetail>($"/alerts/{summary.Id}", Json, Ct))!;
        Assert.Single(alert.Hits);
    }

    [Fact]
    public async Task Rules_endpoint_returns_the_active_rule_set_and_its_version()
    {
        var rules = (await _client.GetFromJsonAsync<RuleSet>("/rules", Json, Ct))!;

        Assert.Equal("v1", rules.Version);
        var r1 = Assert.Single(rules.Rules, r => r.Id == "R1");
        Assert.Equal(60, r1.WindowMinutes);
        Assert.Equal(30, r1.Points);
    }
}
