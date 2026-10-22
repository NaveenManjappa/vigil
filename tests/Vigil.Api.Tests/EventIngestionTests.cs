using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Vigil.Domain.Events;
using Vigil.Infrastructure.Events;

namespace Vigil.Api.Tests;

public sealed class EventIngestionTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateTimeOffset Monday = new(2026, 9, 21, 9, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private readonly HttpClient _client = factory.CreateClient();

    // Each test uses its own account so tests stay independent while sharing one database.
    private static object Event(string accountId, string type, DateTimeOffset at, Guid? id = null) => new
    {
        eventId = id ?? Guid.NewGuid(),
        accountId,
        holderId = $"HOLDER-{accountId}",
        type,
        occurredAt = at,
        deviceId = "D-77",
        ipAddress = "198.51.100.77",
        country = "RO",
        channel = "web",
        attributes = new Dictionary<string, string> { ["NewEmail"] = "x@example.net" },
    };

    private async Task<IReadOnlyList<PortalEvent>> TimelineAsync(string accountId) =>
        (await _client.GetFromJsonAsync<List<PortalEvent>>($"/accounts/{accountId}/timeline", Json, TestContext.Current.CancellationToken))!;

    [Fact]
    public async Task Timeline_is_ordered_by_when_events_occurred_not_when_they_arrived()
    {
        const string account = "ACC-ORDER";
        object[] arrivedOutOfOrder =
        [
            Event(account, "EmailChanged", Monday.AddMinutes(10)),
            Event(account, "PasswordReset", Monday),
            Event(account, "MfaEnrolled", Monday.AddMinutes(4)),
        ];

        var response = await _client.PostAsJsonAsync("/events", arrivedOutOfOrder, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();

        var timeline = await TimelineAsync(account);
        Assert.Equal([EventType.PasswordReset, EventType.MfaEnrolled, EventType.EmailChanged], timeline.Select(e => e.Type));
        Assert.Equal("x@example.net", timeline[0].Attributes["NewEmail"]);
    }

    [Fact]
    public async Task Reposting_the_same_events_is_idempotent()
    {
        const string account = "ACC-IDEMPOTENT";
        object[] batch = [Event(account, "PasswordReset", Monday, Guid.NewGuid()), Event(account, "MfaEnrolled", Monday.AddMinutes(4), Guid.NewGuid())];
        var ct = TestContext.Current.CancellationToken;

        var first = await (await _client.PostAsJsonAsync("/events", batch, ct)).Content.ReadFromJsonAsync<AppendResult>(ct);
        var second = await (await _client.PostAsJsonAsync("/events", batch, ct)).Content.ReadFromJsonAsync<AppendResult>(ct);

        Assert.Equal(new AppendResult(Accepted: 2, AlreadyIngested: 0), first);
        Assert.Equal(new AppendResult(Accepted: 0, AlreadyIngested: 2), second);
        Assert.Equal(2, (await TimelineAsync(account)).Count);
    }

    [Fact]
    public async Task Unknown_event_type_rejects_the_whole_batch()
    {
        const string account = "ACC-INVALID";
        object[] batch = [Event(account, "PasswordReset", Monday), Event(account, "DividendMandateChanged", Monday.AddMinutes(1))];
        var ct = TestContext.Current.CancellationToken;

        var response = await _client.PostAsJsonAsync("/events", batch, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(ct);
        Assert.Contains("[1].Type", problem!.Errors.Keys);
        Assert.Empty(await TimelineAsync(account));
    }
}
