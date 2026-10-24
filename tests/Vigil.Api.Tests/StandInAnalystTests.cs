extern alias simulator;

using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using simulator::Vigil.Simulator.StandIn;
using Vigil.Agents;
using Vigil.Api.Cases;
using Vigil.Domain.Cases;
using Vigil.Domain.Datasets;
using Vigil.Domain.Events;

namespace Vigil.Api.Tests;

/// <summary>The Stand-in Analyst run the way the simulator runs it: post a Dataset, then decide every Case from its Label.</summary>
public sealed class StandInAnalystTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateTimeOffset Monday = new(2026, 9, 21, 9, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static PortalEvent Event(string accountId, EventType type, DateTimeOffset at) => new(
        Guid.NewGuid(), accountId, $"HOLDER-{accountId}", type, at, $"D-{accountId}", "198.51.100.80", "RO", "web",
        new Dictionary<string, string>());

    // R1 + R2 + R3 = 80, High, so each Account opens a Case whatever its Label.
    private static LabelledAccount ReachingHigh(string accountId, Label label) => new(accountId, label,
    [
        Event(accountId, EventType.PasswordReset, Monday),
        Event(accountId, EventType.MfaEnrolled, Monday.AddMinutes(4)),
        Event(accountId, EventType.EmailChanged, Monday.AddMinutes(10)),
        Event(accountId, EventType.BankDetailsChanged, Monday.AddDays(1)),
    ]);

    [Fact]
    public async Task After_posting_a_Dataset_and_deciding_every_Case_is_Closed_with_a_stand_in_decision_from_its_Label()
    {
        var client = factory.CreateClient();
        LabelledAccount[] dataset =
        [
            ReachingHigh("ACC-STANDIN-FRAUD", new Label(Persona.AtoFraudster, FraudKind.Ato)),
            ReachingHigh("ACC-STANDIN-GENUINE", new Label(Persona.HouseMover, null)),
        ];

        Assert.Equal(8, await DatasetPoster.PostAsync(client, dataset, Ct));
        foreach (var account in dataset)
            await InvestigationQueueTests.CaseEventuallyAsync(client, account.AccountId, c => c.Status == CaseStatus.AwaitingApproval);

        var result = await DecideCommand.DecideAsync(client, dataset.ToDictionary(a => a.AccountId, a => a.Label), Ct);

        Assert.Equal(2, result.Decided);
        var cases = (await client.GetFromJsonAsync<List<CaseSummary>>("/cases", Json, Ct))!;
        var decisions = new Dictionary<string, DecisionView?>();
        foreach (var c in cases)
        {
            Assert.Equal(CaseStatus.Closed, c.Status);
            decisions[c.AccountId] = (await client.GetFromJsonAsync<CaseDetail>($"/cases/{c.Id}", Json, Ct))!.Decision;
        }
        Assert.All(decisions.Values, d => Assert.Equal("stand-in", d!.AnalystId));
        Assert.Equal(
            (DecisionOutcome.ConfirmedFraud, Recommendation.BlockBankChanges),
            (decisions["ACC-STANDIN-FRAUD"]!.Outcome, decisions["ACC-STANDIN-FRAUD"]!.Action));
        Assert.Equal(
            (DecisionOutcome.FalsePositive, Recommendation.NoAction),
            (decisions["ACC-STANDIN-GENUINE"]!.Outcome, decisions["ACC-STANDIN-GENUINE"]!.Action));
    }
}
