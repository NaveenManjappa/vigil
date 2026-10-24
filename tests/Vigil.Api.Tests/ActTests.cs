using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Vigil.Agents;
using Vigil.Api.Alerts;
using Vigil.Api.Cases;
using Vigil.Api.Llm;
using Vigil.Domain.Audit;
using Vigil.Domain.Cases;
using Vigil.Domain.Fraud;
using Vigil.Infrastructure.Cases;

namespace Vigil.Api.Tests;

/// <summary>
/// The Act step and the audit trail through the API with the Fake provider: a decision records the Analyst Decision,
/// any Intervention and Audit Entries, and a ConfirmedFraud outcome becomes a Confirmed Fraud.
/// </summary>
public sealed class ActTests(MeteredApiFactory factory) : IClassFixture<MeteredApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<Guid> AwaitingApprovalAsync(HttpClient client, string accountId)
    {
        await InvestigationQueueTests.TakeOverToHighAsync(client, accountId);
        return (await InvestigationQueueTests.CaseEventuallyAsync(client, accountId, c => c.Status == CaseStatus.AwaitingApproval)).Id;
    }

    private static Task<HttpResponseMessage> DecideAsync(HttpClient client, Guid caseId, AnalystDecision decision) =>
        client.PostAsJsonAsync($"/cases/{caseId}/decision", decision, Json, Ct);

    private static async Task<List<AuditEntryView>> AuditAsync(HttpClient client, Guid caseId) =>
        (await client.GetFromJsonAsync<List<AuditEntryView>>($"/cases/{caseId}/audit", Json, Ct))!;

    [Fact]
    public async Task Approving_writes_the_four_audit_kinds_with_actor_model_versions_refs_and_tokens()
    {
        var client = factory.CreateClient();
        var caseId = await AwaitingApprovalAsync(client, "ACC-ACT-AUDIT");
        var cited = (await client.GetFromJsonAsync<CaseDetail>($"/cases/{caseId}", Json, Ct))!.Finding!.EvidenceRefs;

        (await DecideAsync(client, caseId,
            new(DecisionOutcome.FalsePositive, Recommendation.ContactHolder, "analyst-7", "Holder moved house."))).EnsureSuccessStatusCode();

        var audit = await AuditAsync(client, caseId);
        Assert.Equal(
            [AuditEntryKind.InvestigationCompleted, AuditEntryKind.ReviewCompleted, AuditEntryKind.DecisionRecorded, AuditEntryKind.ActionTaken],
            audit.Select(a => a.Kind));

        var investigated = audit[0];
        Assert.Equal(Investigator.AgentId, investigated.Actor);
        Assert.Equal(MeteredChatClient.Model, investigated.Model);
        Assert.Equal("investigator.v1", investigated.PromptVersion);
        // Two round trips, the tool call and the Finding, at 100 in and 20 out each.
        Assert.Equal((200, 40), (investigated.InputTokens, investigated.OutputTokens));

        var reviewed = audit[1];
        Assert.Equal(Reviewer.AgentId, reviewed.Actor);
        Assert.Equal(MeteredChatClient.Model, reviewed.Model);
        Assert.Equal("reviewer.v1", reviewed.PromptVersion);
        Assert.Equal((100, 20), (reviewed.InputTokens, reviewed.OutputTokens));

        Assert.All(audit[2..], a =>
        {
            Assert.Equal("analyst-7", a.Actor);
            Assert.Null(a.Model);
            Assert.Equal("investigator.v1+reviewer.v1", a.PromptVersion);
            Assert.Equal(0, a.InputTokens + a.OutputTokens);
        });

        Assert.All(audit, a =>
        {
            Assert.Equal("v2", a.RulesVersion);
            Assert.Equal(cited.Select(r => (r.Ref, r.EventIds)), a.Refs.Select(r => (r.Ref, r.EventIds)));
            Assert.NotEmpty(a.Refs.SelectMany(r => r.EventIds));
            Assert.True(a.At > DateTimeOffset.UnixEpoch);
        });
        Assert.True(audit.Select(a => a.At).SequenceEqual(audit.Select(a => a.At).Order()));
    }

    [Fact]
    public async Task Blocking_bank_changes_records_an_Intervention_for_seven_days_shown_with_the_decision()
    {
        var client = factory.CreateClient();
        var caseId = await AwaitingApprovalAsync(client, "ACC-ACT-BLOCK");

        var response = await DecideAsync(client, caseId,
            new(DecisionOutcome.FalsePositive, Recommendation.BlockBankChanges, "analyst-9", "Belt and braces."));

        var @case = (await response.Content.ReadFromJsonAsync<CaseDetail>(Json, Ct))!;
        Assert.Equal(CaseStatus.Closed, @case.Status);
        var decision = Assert.IsType<DecisionView>(@case.Decision);
        Assert.Equal((DecisionOutcome.FalsePositive, Recommendation.BlockBankChanges, "analyst-9", "Belt and braces."),
            (decision.Outcome, decision.Action, decision.AnalystId, decision.Notes));
        var intervention = Assert.IsType<InterventionView>(@case.Intervention);
        Assert.Equal(InterventionKind.BlockBankChanges, intervention.Kind);
        Assert.Equal(decision.DecidedAt.AddDays(7), intervention.Until);
        var read = (await client.GetFromJsonAsync<CaseDetail>($"/cases/{caseId}", Json, Ct))!;
        Assert.Equal((decision, intervention), (read.Decision, read.Intervention));
    }

    [Fact]
    public async Task Contacting_the_Holder_records_an_Intervention_with_no_end_date()
    {
        var client = factory.CreateClient();
        var caseId = await AwaitingApprovalAsync(client, "ACC-ACT-CONTACT");

        (await DecideAsync(client, caseId, new(DecisionOutcome.FalsePositive, Recommendation.ContactHolder, "analyst-9", null)))
            .EnsureSuccessStatusCode();

        var intervention = (await client.GetFromJsonAsync<CaseDetail>($"/cases/{caseId}", Json, Ct))!.Intervention;
        Assert.Equal(InterventionKind.ContactHolder, intervention?.Kind);
        Assert.Null(intervention?.Until);
    }

    [Fact]
    public async Task A_FalsePositive_with_NoAction_records_no_Intervention_and_no_Confirmed_Fraud()
    {
        var client = factory.CreateClient();
        var caseId = await AwaitingApprovalAsync(client, "ACC-ACT-FP");

        (await DecideAsync(client, caseId, new(DecisionOutcome.FalsePositive, Recommendation.NoAction, "analyst-7", null)))
            .EnsureSuccessStatusCode();

        var @case = (await client.GetFromJsonAsync<CaseDetail>($"/cases/{caseId}", Json, Ct))!;
        Assert.Equal(DecisionOutcome.FalsePositive, @case.Decision?.Outcome);
        Assert.Null(@case.Intervention);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/accounts/ACC-ACT-FP/confirmed-fraud", Ct)).StatusCode);
    }

    [Fact]
    public async Task A_ConfirmedFraud_decision_records_Confirmed_Fraud_from_the_Analyst_and_lifts_a_Neighbour_by_G2()
    {
        var client = factory.CreateClient();
        var caseId = await AwaitingApprovalAsync(client, "ACC-ACT-CF");
        // A lone login on the same device: no Alert of its own until the device leads to Confirmed Fraud.
        var neighbourLogin = Guid.NewGuid();
        (await client.PostAsJsonAsync("/events", new[]
        {
            new
            {
                eventId = neighbourLogin, accountId = "ACC-ACT-CF-NEIGHBOUR", holderId = "HOLDER-ACC-ACT-CF-NEIGHBOUR", type = "LoginSucceeded",
                occurredAt = new DateTimeOffset(2026, 9, 25, 9, 0, 0, TimeSpan.Zero), deviceId = "D-ACC-ACT-CF", ipAddress = "203.0.113.5",
                country = "GB", channel = "web",
            },
        }, Ct)).EnsureSuccessStatusCode();
        Assert.Null(await NeighbourAlertAsync(client));

        var decided = await DecideAsync(client, caseId,
            new(DecisionOutcome.ConfirmedFraud, Recommendation.BlockBankChanges, "analyst-7", "Holder confirmed it was not them."));

        Assert.Equal(HttpStatusCode.OK, decided.StatusCode);
        var fraud = (await client.GetFromJsonAsync<ConfirmedFraud>("/accounts/ACC-ACT-CF/confirmed-fraud", Json, Ct))!;
        Assert.Equal(ConfirmedFraudSource.Analyst, fraud.Source);
        var @case = (await decided.Content.ReadFromJsonAsync<CaseDetail>(Json, Ct))!;
        Assert.Equal(@case.Decision!.DecidedAt, fraud.DecidedAt);

        var lifted = Assert.IsType<AlertDetail>(await NeighbourAlertAsync(client));
        var g2 = Assert.Single(lifted.Hits, h => h.RuleId == "G2");
        Assert.Contains(neighbourLogin, g2.EvidenceEventIds);
        Assert.Contains("ACC-ACT-CF", g2.Explanation);
    }

    [Fact]
    public async Task Two_decisions_at_once_record_one_and_refuse_the_other()
    {
        var client = factory.CreateClient();
        var caseId = await AwaitingApprovalAsync(client, "ACC-ACT-RACE");

        var responses = await Task.WhenAll(
            DecideAsync(client, caseId, new(DecisionOutcome.ConfirmedFraud, Recommendation.BlockBankChanges, "analyst-1", null)),
            DecideAsync(client, caseId, new(DecisionOutcome.FalsePositive, Recommendation.NoAction, "analyst-2", null)));

        var statuses = responses.Select(r => r.StatusCode).Order().ToList();
        if (!statuses.SequenceEqual([HttpStatusCode.OK, HttpStatusCode.Conflict]))
            Assert.Fail(string.Join("\n", await Task.WhenAll(responses.Select(async r => $"{r.StatusCode}: {await r.Content.ReadAsStringAsync(Ct)}"))));
        var winner = (await responses.Single(r => r.IsSuccessStatusCode).Content.ReadFromJsonAsync<CaseDetail>(Json, Ct))!.Decision!;
        var @case = (await client.GetFromJsonAsync<CaseDetail>($"/cases/{caseId}", Json, Ct))!;
        Assert.Equal(winner, @case.Decision);
        // The loser left no trace: one decision's pair of entries, and Confirmed Fraud only if the winner chose it.
        Assert.Equal(2, (await AuditAsync(client, caseId)).Count(a => a.Kind is AuditEntryKind.DecisionRecorded or AuditEntryKind.ActionTaken));
        var fraud = await client.GetAsync("/accounts/ACC-ACT-RACE/confirmed-fraud", Ct);
        Assert.Equal(winner.Outcome == DecisionOutcome.ConfirmedFraud ? HttpStatusCode.OK : HttpStatusCode.NotFound, fraud.StatusCode);
    }

    [Fact]
    public async Task A_decision_whose_Act_runs_after_another_has_committed_is_refused_and_leaves_the_first_in_place()
    {
        var client = factory.CreateClient();
        var caseId = await AwaitingApprovalAsync(client, "ACC-ACT-LATE");
        (await DecideAsync(client, caseId, new(DecisionOutcome.FalsePositive, Recommendation.NoAction, "analyst-1", null))).EnsureSuccessStatusCode();

        // A racing decision that passed the status check before the first closed the Case gets this far.
        using var scope = factory.Services.CreateScope();
        var act = scope.ServiceProvider.GetRequiredService<IActStep>();
        await Assert.ThrowsAsync<DecisionAlreadyRecordedException>(() =>
            act.ActAsync(caseId, new(DecisionOutcome.ConfirmedFraud, Recommendation.BlockBankChanges, "analyst-2", null), Ct));

        var @case = (await client.GetFromJsonAsync<CaseDetail>($"/cases/{caseId}", Json, Ct))!;
        Assert.Equal("analyst-1", @case.Decision!.AnalystId);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/accounts/ACC-ACT-LATE/confirmed-fraud", Ct)).StatusCode);
    }

    private static async Task<AlertDetail?> NeighbourAlertAsync(HttpClient client)
    {
        var summary = (await client.GetFromJsonAsync<List<AlertSummary>>("/alerts", Json, Ct))!
            .SingleOrDefault(a => a.AccountId == "ACC-ACT-CF-NEIGHBOUR");
        return summary is null ? null : await client.GetFromJsonAsync<AlertDetail>($"/alerts/{summary.Id}", Json, Ct);
    }
}

/// <summary>The Fake provider, metered: every call reports 100 input and 20 output tokens under its own model name.</summary>
public sealed class MeteredChatClient(IChatClient inner) : DelegatingChatClient(inner)
{
    public const string Model = "metered-fake";

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var response = await base.GetResponseAsync(messages, options, cancellationToken);
        response.ModelId = Model;
        response.Usage = new UsageDetails { InputTokenCount = 100, OutputTokenCount = 20, TotalTokenCount = 120 };
        return response;
    }
}

public sealed class MeteredApiFactory : ApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(s => s.Replace(ServiceDescriptor.Singleton<IChatClient>(new MeteredChatClient(new FakeChatClient()))));
    }
}
