using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Vigil.Api.Tests;

/// <summary>
/// The walkthrough in docs/user-guide.md, one call per step, with the Fake model. If this test fails, the guide is wrong.
/// The request bodies here are the ones the guide tells you to paste into Scalar.
/// </summary>
public sealed class WalkthroughTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly HttpClient _http = api.CreateClient();

    private async Task<JsonNode> PostAsync(string path, object? body = null)
    {
        var response = await _http.PostAsJsonAsync(path, body ?? new { }, Ct);
        Assert.True(response.IsSuccessStatusCode, $"{path}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync(Ct)}");
        return (await response.Content.ReadFromJsonAsync<JsonNode>(Ct))!;
    }

    private async Task<JsonNode> GetAsync(string path) => (await _http.GetFromJsonAsync<JsonNode>(path, Ct))!;

    private Task<JsonNode> SendEventAsync(string accountId, string holderId, string type, string occurredAt,
        string device, string ip, string country, Dictionary<string, string>? attributes = null) =>
        PostAsync("/events", new { accountId, holderId, type, occurredAt, deviceId = device, ipAddress = ip, country, attributes });

    private static string[] RuleIds(JsonNode? hits) => [.. hits!.AsArray().Select(h => h!["ruleId"]!.GetValue<string>())];

    [Fact]
    public async Task Margarets_takeover_from_first_event_to_closed_Case()
    {
        // Step 0: start from nothing.
        await PostAsync("/reset");

        // Step 1: Derek logs in from device D-77. His first event, so nothing is compared and nothing hits.
        var derek = await SendEventAsync("ACC-DEREK", "HOLDER-DEREK", "LoginSucceeded", "2026-09-01T10:00:00Z", "D-77", "198.51.100.77", "RO");
        Assert.Equal("Event #1 added no Rule Hits.", derek["message"]!.GetValue<string>());

        // Step 2: Derek is known fraud.
        await PostAsync("/accounts/ACC-DEREK/confirm-fraud");

        // Steps 3 and 4: Margaret's usual logins, from her laptop in Great Britain.
        await SendEventAsync("ACC-MARGARET", "HOLDER-MARGARET", "LoginSucceeded", "2026-09-01T08:30:00Z", "D-MARGARET-LAPTOP", "203.0.113.51", "GB");
        var usual = await SendEventAsync("ACC-MARGARET", "HOLDER-MARGARET", "LoginSucceeded", "2026-09-15T08:30:00Z", "D-MARGARET-LAPTOP", "203.0.113.51", "GB");
        Assert.Equal("Event #2 added no Rule Hits.", usual["message"]!.GetValue<string>());
        Assert.Null(usual["alert"]);

        // Step 5: a password reset from Derek's device in Romania. A stranger, and a device known fraud uses.
        var reset = await SendEventAsync("ACC-MARGARET", "HOLDER-MARGARET", "PasswordReset", "2026-09-22T09:00:00Z", "D-77", "198.51.100.77", "RO");
        Assert.Equal("Event #3 added R0 (+10), G2 (+25). Alert: Score 35, Medium.", reset["message"]!.GetValue<string>());

        // Step 6: MFA enrolled four minutes later. R1 takes the Alert to High, and a Case opens.
        var mfa = await SendEventAsync("ACC-MARGARET", "HOLDER-MARGARET", "MfaEnrolled", "2026-09-22T09:04:00Z", "D-77", "198.51.100.77", "RO");
        Assert.Equal(["R1"], RuleIds(mfa["newHits"]));
        Assert.Equal(["R0", "G2"], mfa["repeatedRules"]!.AsArray().Select(r => r!.GetValue<string>()));
        Assert.Equal(65, mfa["alert"]!["score"]!.GetValue<int>());
        Assert.Equal("High", mfa["alert"]!["band"]!.GetValue<string>());
        var caseId = mfa["caseOpened"]!.GetValue<Guid>();

        // Step 7: the email address changes.
        var email = await SendEventAsync("ACC-MARGARET", "HOLDER-MARGARET", "EmailChanged", "2026-09-22T09:10:00Z", "D-77", "198.51.100.77", "RO",
            new() { ["NewEmail"] = "m.attacker@example.net" });
        Assert.Equal(["R2"], RuleIds(email["newHits"]));
        Assert.Equal(85, email["alert"]!["score"]!.GetValue<int>());
        Assert.Null(email["caseOpened"]);

        // Step 8: the next day, the bank details change. The Alert is Critical.
        var bank = await SendEventAsync("ACC-MARGARET", "HOLDER-MARGARET", "BankDetailsChanged", "2026-09-23T14:10:00Z", "D-77", "198.51.100.77", "RO",
            new() { ["NewBankAccountHash"] = "BA-9f31" });
        var r3 = Assert.Single(bank["newHits"]!.AsArray())!;
        Assert.Equal("Contact change then bank-detail change, 29h apart; window 72h", r3["explanation"]!.GetValue<string>());
        Assert.Equal(115, bank["alert"]!["score"]!.GetValue<int>());
        Assert.Equal("Critical", bank["alert"]!["band"]!.GetValue<string>());

        // Step 9: the account view shows it all in one place.
        var margaret = await GetAsync("/accounts/ACC-MARGARET");
        Assert.Equal([1, 2, 3, 4, 5, 6], margaret["events"]!.AsArray().Select(e => e!["number"]!.GetValue<int>()));
        Assert.Equal("Open", margaret["cases"]![0]!["status"]!.GetValue<string>());
        var d77 = margaret["linkedIdentifiers"]!.AsArray().Single(i => i!["value"]!.GetValue<string>() == "D-77")!;
        Assert.Equal("ACC-DEREK", d77["otherAccounts"]![0]!["accountId"]!.GetValue<string>());

        // Step 10: the Investigator writes a Finding that cites events by number.
        var investigated = await PostAsync($"/cases/{caseId}/investigate");
        Assert.Equal("AwaitingDecision", investigated["status"]!.GetValue<string>());
        Assert.Equal("LikelyAccountTakeover", investigated["finding"]!["verdict"]!.GetValue<string>());
        Assert.Equal([3, 4, 5, 6], investigated["finding"]!["events"]!.AsArray().Select(e => e!.GetValue<int>()));

        // Step 11: the analyst decides, which closes the Case and blocks bank changes for 7 days.
        var decided = await PostAsync($"/cases/{caseId}/decision",
            new { outcome = "ConfirmedFraud", action = "BlockBankChanges", analystId = "analyst-1", notes = "Matches the takeover pattern." });
        Assert.Equal("Closed", decided["status"]!.GetValue<string>());
        Assert.Equal("BlockBankChanges", decided["intervention"]!.GetValue<string>());
        Assert.Equal(decided["decidedAt"]!.GetValue<DateTimeOffset>().AddDays(7), decided["interventionUntil"]!.GetValue<DateTimeOffset>());
        Assert.True((await GetAsync("/accounts/ACC-MARGARET"))["isConfirmedFraud"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Mr_Patel_moving_house_reaches_Medium_and_opens_no_Case()
    {
        // Step 12: a genuine address change then bank change, from his own laptop.
        await SendEventAsync("ACC-PATEL", "HOLDER-PATEL", "LoginSucceeded", "2026-09-10T19:00:00Z", "D-PATEL-LAPTOP", "203.0.113.20", "GB");
        await SendEventAsync("ACC-PATEL", "HOLDER-PATEL", "AddressChanged", "2026-09-20T18:00:00Z", "D-PATEL-LAPTOP", "203.0.113.20", "GB",
            new() { ["NewPostcode"] = "RG1 4AB" });
        var bank = await SendEventAsync("ACC-PATEL", "HOLDER-PATEL", "BankDetailsChanged", "2026-09-21T19:00:00Z", "D-PATEL-LAPTOP", "203.0.113.20", "GB",
            new() { ["NewBankAccountHash"] = "BA-PATEL-NEW" });

        Assert.Equal("Event #3 added R3 (+30). Alert: Score 30, Medium.", bank["message"]!.GetValue<string>());
        Assert.Null(bank["caseOpened"]);
    }

    [Fact]
    public async Task A_bad_event_gets_an_error_for_each_field_and_changes_nothing()
    {
        var response = await _http.PostAsJsonAsync("/events", new { accountId = "ACC-X", type = "Teleported", country = "gb" }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await response.Content.ReadFromJsonAsync<JsonNode>(Ct))!["errors"]!.AsObject();
        Assert.Equal(["holderId", "occurredAt", "type", "country"], errors.Select(e => e.Key));
    }

    [Fact]
    public async Task Sending_the_same_event_twice_changes_nothing_the_second_time()
    {
        var body = new
        {
            eventId = Guid.NewGuid(), accountId = "ACC-TWICE", holderId = "HOLDER-TWICE", type = "LoginSucceeded",
            occurredAt = "2026-09-01T10:00:00Z", deviceId = "D-1", country = "GB",
        };
        await PostAsync("/events", body);

        var again = await PostAsync("/events", body);

        Assert.Equal("Event #1 was already ingested; nothing changed.", again["message"]!.GetValue<string>());
        Assert.Single((await GetAsync("/accounts/ACC-TWICE"))["events"]!.AsArray());
    }

    [Fact]
    public async Task A_Case_can_only_be_decided_after_it_is_investigated()
    {
        await SendEventAsync("ACC-EARLY", "HOLDER-EARLY", "LoginSucceeded", "2026-09-01T08:00:00Z", "D-1", "203.0.113.1", "GB");
        await SendEventAsync("ACC-EARLY", "HOLDER-EARLY", "PasswordReset", "2026-09-02T08:00:00Z", "D-2", "198.51.100.2", "FR");
        var mfa = await SendEventAsync("ACC-EARLY", "HOLDER-EARLY", "MfaEnrolled", "2026-09-02T08:05:00Z", "D-2", "198.51.100.2", "FR");
        var caseId = mfa["caseOpened"]!.GetValue<Guid>();

        var response = await _http.PostAsJsonAsync($"/cases/{caseId}/decision",
            new { outcome = "FalsePositive", action = "NoAction", analystId = "analyst-1" }, Ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }
}
