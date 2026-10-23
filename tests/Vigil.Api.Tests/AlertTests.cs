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

    // Each Account has its own device unless one is given, so G3 does not join every Account in the class into one Ring.
    private static (Guid Id, object Body) Event(
        string accountId, string type, DateTimeOffset at, Dictionary<string, string>? attributes = null, string? deviceId = null)
    {
        var id = Guid.NewGuid();
        return (id, new
        {
            eventId = id,
            accountId,
            holderId = $"HOLDER-{accountId}",
            type,
            occurredAt = at,
            deviceId = deviceId ?? $"D-{accountId}",
            ipAddress = "198.51.100.77",
            country = "RO",
            channel = "web",
            attributes,
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
        Assert.Equal("v2", alert.RulesVersion);
        var hit = Assert.Single(alert.Hits);
        Assert.Equal("R1", hit.RuleId);
        Assert.Equal(30, hit.Points);
        Assert.Equal([reset.Id, mfa.Id], hit.EvidenceEventIds);
        Assert.Equal(Monday.AddMinutes(4), hit.TriggeredAt);
        Assert.Equal("Password reset then MFA enrolment, 4 min apart; window 1h", hit.Explanation);
    }

    [Fact]
    public async Task A_takeover_through_to_a_sale_raises_one_alert_with_a_hit_per_rule()
    {
        const string account = "ACC-MULTI";
        var reset = Event(account, "PasswordReset", Monday);
        var mfa = Event(account, "MfaEnrolled", Monday.AddMinutes(4));
        var email = Event(account, "EmailChanged", Monday.AddMinutes(10));
        var bank = Event(account, "BankDetailsChanged", Monday.AddDays(1));
        var sale = Event(account, "SaleRequested", Monday.AddDays(2));

        await PostAsync(reset.Body, mfa.Body, email.Body);
        await PostAsync(bank.Body, sale.Body);

        var summary = Assert.Single(await AlertsAsync(), a => a.AccountId == account);
        var alert = (await _client.GetFromJsonAsync<AlertDetail>($"/alerts/{summary.Id}", Json, Ct))!;

        Assert.Equal(120, alert.Score);
        Assert.Equal(RiskBand.Critical, alert.Band);
        Assert.Collection(alert.Hits,
            h => AssertHit(h, "R1", 30, [reset.Id, mfa.Id], "Password reset then MFA enrolment, 4 min apart; window 1h"),
            h => AssertHit(h, "R2", 20, [reset.Id, email.Id], "Password reset then contact change, 10 min apart; window 24h"),
            h => AssertHit(h, "R3", 30, [email.Id, bank.Id], "Contact change then bank-detail change, 23h 50 min apart; window 72h"),
            h => AssertHit(h, "R4", 40, [bank.Id, sale.Id], "Bank-detail change then sale or transfer, 24h apart; window 7d"));
    }

    private static void AssertHit(RuleHit hit, string ruleId, int points, Guid[] evidence, string explanation)
    {
        Assert.Equal(ruleId, hit.RuleId);
        Assert.Equal(points, hit.Points);
        Assert.Equal(evidence, hit.EvidenceEventIds);
        Assert.Equal(explanation, hit.Explanation);
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
    public async Task A_late_event_leaves_the_stored_hits_unchanged()
    {
        const string account = "ACC-LATE";
        var reset = Event(account, "PasswordReset", Monday);
        var mfa = Event(account, "MfaEnrolled", Monday.AddMinutes(20));
        await PostAsync(reset.Body, mfa.Body);

        // An earlier MFA enrolment the source delayed: it would be a better R1 hit, but R1 is already stored.
        await PostAsync(Event(account, "MfaEnrolled", Monday.AddMinutes(5)).Body);

        var summary = Assert.Single(await AlertsAsync(), a => a.AccountId == account);
        var alert = (await _client.GetFromJsonAsync<AlertDetail>($"/alerts/{summary.Id}", Json, Ct))!;
        AssertHit(Assert.Single(alert.Hits), "R1", 30, [reset.Id, mfa.Id], "Password reset then MFA enrolment, 20 min apart; window 1h");
        Assert.Equal(30, alert.Score);
    }

    [Fact]
    public async Task A_hit_after_a_quiet_risk_period_opens_a_second_alert_and_leaves_the_first_alone()
    {
        const string account = "ACC-EPISODES";
        await PostAsync(Event(account, "PasswordReset", Monday).Body, Event(account, "MfaEnrolled", Monday.AddMinutes(4)).Body);
        await PostAsync(Event(account, "PasswordReset", Monday.AddDays(10)).Body, Event(account, "EmailChanged", Monday.AddDays(10).AddMinutes(9)).Body);

        var alerts = (await AlertsAsync()).Where(a => a.AccountId == account).OrderBy(a => a.Score).ToList();

        Assert.Equal([20, 30], alerts.Select(a => a.Score));
        var first = (await _client.GetFromJsonAsync<AlertDetail>($"/alerts/{alerts[1].Id}", Json, Ct))!;
        Assert.Equal("R1", Assert.Single(first.Hits).RuleId);
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

        Assert.Equal("v2", rules.Version);
        Assert.Equal(["R0", "R1", "R2", "R3", "R4", "R5", "R6", "G1", "G3"], rules.Rules.Select(r => r.Id));
        var r1 = Assert.Single(rules.Rules, r => r.Id == "R1");
        Assert.Equal(60, r1.WindowMinutes);
        Assert.Equal(30, r1.Points);
        var g1 = Assert.Single(rules.Rules, r => r.Id == "G1");
        Assert.Equal(RuleKind.Graph, g1.Kind);
        Assert.Equal(30, g1.Points);
        var g3 = Assert.Single(rules.Rules, r => r.Id == "G3");
        Assert.Equal(20, g3.Points);
        Assert.Equal(3, g3.AccountThreshold);
    }

    [Fact]
    public async Task The_second_Account_to_pay_out_to_another_Holders_bank_account_gets_G1()
    {
        var bankAccount = new Dictionary<string, string> { ["NewBankAccountHash"] = "BA-SHARED-G1" };
        var first = Event("ACC-G1-FIRST", "BankDetailsChanged", Monday, bankAccount);
        var second = Event("ACC-G1-SECOND", "BankDetailsChanged", Monday.AddDays(1), bankAccount);

        await PostAsync(first.Body);
        await PostAsync(second.Body);

        var summary = Assert.Single(await AlertsAsync(), a => a.AccountId == "ACC-G1-SECOND");
        var alert = (await _client.GetFromJsonAsync<AlertDetail>($"/alerts/{summary.Id}", Json, Ct))!;

        // G1 30 alone: a lone bank change completes no sequence Rule and, as the Account's first event, is no First Sighting.
        Assert.Equal(30, alert.Score);
        Assert.Equal(RiskBand.Medium, alert.Band);
        Assert.Equal("v2", alert.RulesVersion);
        var hit = Assert.Single(alert.Hits);
        AssertHit(hit, "G1", 30, [first.Id, second.Id],
            "Pays out to bank account BA-SHARED-G1, which ACC-G1-FIRST of a different Holder also pays out to");
        Assert.Equal(Monday.AddDays(1), hit.TriggeredAt);
    }

    [Fact]
    public async Task The_first_Account_on_a_shared_bank_account_is_lifted_when_another_Holders_Account_links_to_it()
    {
        var bankAccount = new Dictionary<string, string> { ["NewBankAccountHash"] = "BA-SHARED-NEIGHBOUR" };
        var reset = Event("ACC-NEIGHBOUR-A", "PasswordReset", Monday);
        var mfa = Event("ACC-NEIGHBOUR-A", "MfaEnrolled", Monday.AddMinutes(4));
        var aBank = Event("ACC-NEIGHBOUR-A", "BankDetailsChanged", Monday.AddHours(1), bankAccount);
        await PostAsync(reset.Body, mfa.Body, aBank.Body);
        var before = await AlertForAsync("ACC-NEIGHBOUR-A");

        var bBank = Event("ACC-NEIGHBOUR-B", "BankDetailsChanged", Monday.AddDays(1), bankAccount);
        await PostAsync(bBank.Body);
        var after = await AlertForAsync("ACC-NEIGHBOUR-A");

        // R1 30, then G1 30 when B's link completed the pair a day later: 60, High.
        Assert.Equal(before.Id, after.Id);
        Assert.Equal(60, after.Score);
        Assert.Equal(RiskBand.High, after.Band);
        Assert.Equal(2, after.Hits.Count);
        Assert.Equivalent(Assert.Single(before.Hits), after.Hits[0], strict: true);
        AssertHit(after.Hits[1], "G1", 30, [aBank.Id, bBank.Id],
            "Pays out to bank account BA-SHARED-NEIGHBOUR, which ACC-NEIGHBOUR-B of a different Holder also pays out to");
        Assert.Equal(Monday.AddDays(1), after.Hits[1].TriggeredAt);
    }

    [Fact]
    public async Task Two_Accounts_linking_to_each_other_at_the_same_time_both_get_G1()
    {
        // Each request locks its own Account and then the other's, so they may deadlock (timing decides); whether
        // they do or one simply waits, both Accounts must end with G1.
        var bankAccount = new Dictionary<string, string> { ["NewBankAccountHash"] = "BA-SHARED-RACE" };
        await Task.WhenAll(
            PostAsync(Event("ACC-RACE-A", "BankDetailsChanged", Monday, bankAccount).Body),
            PostAsync(Event("ACC-RACE-B", "BankDetailsChanged", Monday.AddMinutes(1), bankAccount).Body));

        Assert.Equal("G1", Assert.Single((await AlertForAsync("ACC-RACE-A")).Hits).RuleId);
        Assert.Equal("G1", Assert.Single((await AlertForAsync("ACC-RACE-B")).Hits).RuleId);
    }

    [Fact]
    public async Task Every_member_of_a_five_Account_Ring_reaches_High()
    {
        // The Ring Persona: each Account registers on one device and pays out to one bank account, an hour apart.
        var bankAccount = new Dictionary<string, string> { ["NewBankAccountHash"] = "BA-RING" };
        var members = Enumerable.Range(1, 5).Select(i =>
        {
            var account = $"ACC-RING-{i}";
            var at = Monday.AddHours(i);
            return (Register: Event(account, "AccountRegistered", at, deviceId: "D-RING"),
                    Bank: Event(account, "BankDetailsChanged", at.AddMinutes(30), bankAccount, deviceId: "D-RING"));
        }).ToList();

        foreach (var member in members) await PostAsync(member.Register.Body, member.Bank.Body);

        // R5 30 on each Account alone is Medium; G1 30 and G3 20 lift every member to 80, High, including the first
        // two, which the neighbour re-check reaches when later members link.
        for (var i = 1; i <= 5; i++)
        {
            var alert = await AlertForAsync($"ACC-RING-{i}");
            Assert.Equal(["G1", "G3", "R5"], alert.Hits.Select(h => h.RuleId).Order());
            Assert.Equal(80, alert.Score);
            Assert.Equal(RiskBand.High, alert.Band);
        }

        // The first member joined a Ring when the third registered on D-RING, before the bank account reached three.
        // A stored hit is never rewritten, so its explanation keeps the count from when it was made.
        var first = await AlertForAsync("ACC-RING-1");
        var g3 = Assert.Single(first.Hits, h => h.RuleId == "G3");
        Assert.Equal([members[0].Register.Id, members[1].Register.Id, members[2].Register.Id], g3.EvidenceEventIds);
        Assert.Equal(Monday.AddHours(3), g3.TriggeredAt);
        Assert.Equal("Device D-RING is shared by 3 Accounts; Ring threshold 3", g3.Explanation);
    }

    private async Task<AlertDetail> AlertForAsync(string accountId)
    {
        var summary = Assert.Single(await AlertsAsync(), a => a.AccountId == accountId);
        return (await _client.GetFromJsonAsync<AlertDetail>($"/alerts/{summary.Id}", Json, Ct))!;
    }
}
