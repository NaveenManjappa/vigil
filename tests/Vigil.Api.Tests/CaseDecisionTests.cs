using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Vigil.Agents;
using Vigil.Api.Cases;
using Vigil.Domain.Cases;

namespace Vigil.Api.Tests;

/// <summary>
/// A Case's Investigation through the API with the Fake provider: it pauses in AwaitingApproval, the analyst reads the
/// unmasked Finding and decides, and the decision resumes the workflow from its Postgres checkpoint.
/// </summary>
public sealed class CaseDecisionTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateTimeOffset Monday = new(2026, 9, 21, 9, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private static readonly AnalystDecision Decision =
        new(DecisionOutcome.ConfirmedFraud, Recommendation.ContactHolder, "analyst-7", "Holder confirmed it was not them.");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static object Event(Guid eventId, string accountId, string type, DateTimeOffset at) => new
    {
        eventId,
        accountId,
        holderId = $"HOLDER-{accountId}",
        type,
        occurredAt = at,
        deviceId = $"D-{accountId}",
        ipAddress = "198.51.100.79",
        country = "RO",
        channel = "web",
    };

    // R1 + R2 + R3 = 80, High; returns the EventIds so refs can be checked against them.
    private static async Task<Guid[]> TakeOverToHighAsync(HttpClient client, string accountId)
    {
        Guid[] ids = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];
        var batch = new[]
        {
            Event(ids[0], accountId, "PasswordReset", Monday),
            Event(ids[1], accountId, "MfaEnrolled", Monday.AddMinutes(4)),
            Event(ids[2], accountId, "EmailChanged", Monday.AddMinutes(10)),
            Event(ids[3], accountId, "BankDetailsChanged", Monday.AddDays(1)),
        };
        (await client.PostAsJsonAsync("/events", batch, Ct)).EnsureSuccessStatusCode();
        return ids;
    }

    private static async Task<CaseDetail> DetailAsync(HttpClient client, Guid caseId) =>
        (await client.GetFromJsonAsync<CaseDetail>($"/cases/{caseId}", Json, Ct))!;

    private static Task<HttpResponseMessage> DecideAsync(HttpClient client, Guid caseId) =>
        client.PostAsJsonAsync($"/cases/{caseId}/decision", Decision, Json, Ct);

    private static async Task<Guid> AwaitingApprovalAsync(HttpClient client, string accountId) =>
        (await InvestigationQueueTests.CaseEventuallyAsync(client, accountId, c => c.Status == CaseStatus.AwaitingApproval)).Id;

    [Fact]
    public async Task A_high_alerts_case_waits_for_approval_with_an_unmasked_Finding_citing_its_events()
    {
        var client = factory.CreateClient();
        var eventIds = await TakeOverToHighAsync(client, "ACC-DECIDE-READ");

        var @case = await DetailAsync(client, await AwaitingApprovalAsync(client, "ACC-DECIDE-READ"));

        Assert.False(@case.InvestigationFailed);
        Assert.False(@case.ReviewFlagged);
        Assert.Equal("investigator.v1+reviewer.v1", @case.PromptVersion);
        var finding = Assert.IsType<AnalystFinding>(@case.Finding);
        Assert.Equal(Recommendation.BlockBankChanges, finding.Recommendation);
        // The Fake names the Account by its Pseudonym; the analyst sees the real AccountId.
        Assert.Contains("ACC-DECIDE-READ", finding.Summary);
        Assert.DoesNotContain("ACCOUNT-1", finding.Summary);
        Assert.NotEmpty(finding.Evidence);
        Assert.All(finding.Evidence, e => Assert.All(e.EventIds, id => Assert.Contains(id, eventIds)));
    }

    [Fact]
    public async Task A_decision_closes_the_case_and_a_second_one_is_refused()
    {
        var client = factory.CreateClient();
        await TakeOverToHighAsync(client, "ACC-DECIDE-CLOSE");
        var caseId = await AwaitingApprovalAsync(client, "ACC-DECIDE-CLOSE");

        var decided = await DecideAsync(client, caseId);
        var again = await DecideAsync(client, caseId);

        Assert.Equal(HttpStatusCode.OK, decided.StatusCode);
        Assert.Equal(CaseStatus.Closed, (await DetailAsync(client, caseId)).Status);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task A_decision_on_an_unknown_case_is_not_found()
    {
        var response = await DecideAsync(factory.CreateClient(), Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_decision_without_an_analyst_is_rejected()
    {
        var client = factory.CreateClient();
        await TakeOverToHighAsync(client, "ACC-DECIDE-ANON");
        var caseId = await AwaitingApprovalAsync(client, "ACC-DECIDE-ANON");

        var response = await client.PostAsJsonAsync($"/cases/{caseId}/decision", Decision with { AnalystId = " " }, Json, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(CaseStatus.AwaitingApproval, (await DetailAsync(client, caseId)).Status);
    }
}

/// <summary>Its own containers, so the only hosts on them are the two this test starts and stops.</summary>
public sealed class CaseRestartTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_second_host_resumes_the_paused_Investigation_from_Postgres_and_accepts_the_decision()
    {
        Guid caseId;
        await using (var first = factory.WithWebHostBuilder(_ => { }))
        {
            var client = first.CreateClient();
            await InvestigationQueueTests.TakeOverToHighAsync(client, "ACC-RESTART");
            caseId = (await InvestigationQueueTests.CaseEventuallyAsync(client, "ACC-RESTART", c => c.Status == CaseStatus.AwaitingApproval)).Id;
        }

        // A model that fails every call: resuming must not investigate again.
        await using var second = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
            s.Replace(ServiceDescriptor.Singleton<IChatClient>(new UnreachableChatClient()))));
        var restarted = second.CreateClient();

        var response = await restarted.PostAsJsonAsync($"/cases/{caseId}/decision",
            new AnalystDecision(DecisionOutcome.FalsePositive, Recommendation.NoAction, "analyst-7", null), Json, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(CaseStatus.Closed, (await restarted.GetFromJsonAsync<CaseDetail>($"/cases/{caseId}", Json, Ct))!.Status);
    }
}

public sealed class UnreachableChatClient : IChatClient
{
    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("No model call is expected here.");

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("No model call is expected here.");

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}
