using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Vigil.Api.Cases;
using Vigil.Domain.Cases;
using Vigil.Domain.Detection;

namespace Vigil.Api.Tests;

public sealed class CaseTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateTimeOffset Monday = new(2026, 9, 21, 9, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private readonly HttpClient _client = factory.CreateClient();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static object Event(string accountId, string type, DateTimeOffset at) => new
    {
        eventId = Guid.NewGuid(),
        accountId,
        holderId = $"HOLDER-{accountId}",
        type,
        occurredAt = at,
        deviceId = "D-77",
        ipAddress = "198.51.100.77",
        country = "RO",
        channel = "web",
    };

    private async Task PostAsync(params object[] batch) =>
        (await _client.PostAsJsonAsync("/events", batch, Ct)).EnsureSuccessStatusCode();

    private async Task<List<CaseSummary>> CasesAsync(string query = "") =>
        (await _client.GetFromJsonAsync<List<CaseSummary>>($"/cases{query}", Json, Ct))!;

    private async Task<List<CaseSummary>> CasesForAsync(string accountId) =>
        (await CasesAsync()).Where(c => c.AccountId == accountId).ToList();

    // R1 + R2 + R3: reset, MFA enrolment and email change, then a bank change the next day.
    private Task TakeOverToHighAsync(string accountId) => PostAsync(
        Event(accountId, "PasswordReset", Monday),
        Event(accountId, "MfaEnrolled", Monday.AddMinutes(4)),
        Event(accountId, "EmailChanged", Monday.AddMinutes(10)),
        Event(accountId, "BankDetailsChanged", Monday.AddDays(1)));

    [Fact]
    public async Task An_alert_reaching_high_opens_one_case()
    {
        const string account = "ACC-CASE-HIGH";
        await TakeOverToHighAsync(account);

        var @case = Assert.Single(await CasesForAsync(account));
        Assert.Equal(CaseStatus.Open, @case.Status);
        Assert.Equal(RiskBand.High, @case.Band);
        Assert.Equal(80, @case.Score);
    }

    [Fact]
    public async Task An_alert_going_on_to_critical_keeps_its_one_case_at_the_higher_priority()
    {
        const string account = "ACC-CASE-CRITICAL";
        await TakeOverToHighAsync(account);
        var opened = Assert.Single(await CasesForAsync(account));

        await PostAsync(Event(account, "SaleRequested", Monday.AddDays(2)));

        var @case = Assert.Single(await CasesForAsync(account));
        Assert.Equal(opened.Id, @case.Id);
        Assert.Equal(opened.OpenedAt, @case.OpenedAt);
        Assert.Equal(RiskBand.Critical, @case.Band);
        Assert.Equal(120, @case.Score);
    }

    [Fact]
    public async Task An_alert_that_stays_medium_opens_no_case()
    {
        const string account = "ACC-CASE-MEDIUM";
        await PostAsync(Event(account, "PasswordReset", Monday), Event(account, "MfaEnrolled", Monday.AddMinutes(4)));

        Assert.Empty(await CasesForAsync(account));
    }

    [Fact]
    public async Task The_open_queue_is_ordered_by_band_then_score_then_time_opened()
    {
        // Opened lowest priority first, so time opened alone would give exactly the wrong order.
        const string high60 = "ACC-Q-HIGH-60", high80First = "ACC-Q-HIGH-80-A", high80Second = "ACC-Q-HIGH-80-B", critical = "ACC-Q-CRIT";

        // R5 + R1: a fast-tracked registration that is then recovered.
        await PostAsync(
            Event(high60, "AccountRegistered", Monday),
            Event(high60, "BankDetailsChanged", Monday.AddHours(1)),
            Event(high60, "PasswordReset", Monday.AddHours(2)),
            Event(high60, "MfaEnrolled", Monday.AddHours(2).AddMinutes(5)));
        await TakeOverToHighAsync(high80First);
        await TakeOverToHighAsync(high80Second);
        await TakeOverToHighAsync(critical);
        await PostAsync(Event(critical, "SaleRequested", Monday.AddDays(2)));

        string[] ours = [high60, high80First, high80Second, critical];
        var queue = (await CasesAsync("?status=Open")).Where(c => ours.Contains(c.AccountId)).ToList();

        Assert.Equal([critical, high80First, high80Second, high60], queue.Select(c => c.AccountId));
        Assert.Equal([120, 80, 80, 60], queue.Select(c => c.Score));
        Assert.All(queue, c => Assert.Equal(CaseStatus.Open, c.Status));
    }
}
