using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Vigil.Domain.Detection;
using Vigil.Domain.Events;
using Vigil.Domain.Graph;

namespace Vigil.Agents.Tests;

public sealed class InvestigatorTests
{
    private static readonly DateTimeOffset Monday = new(2026, 9, 21, 9, 0, 0, TimeSpan.Zero);
    private static readonly BandThresholds Bands = new(Medium: 30, High: 60, Critical: 90);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static PortalEvent Event(EventType type, DateTimeOffset at, string device, string country) =>
        new(Guid.NewGuid(), "ACC-MARGARET", "HOLDER-MARGARET", type, at, device, "198.51.100.77", country, "Web", new Dictionary<string, string>());

    // Margaret's timeline: two usual logins (#1, #2), then a reset (#3) and MFA enrolment (#4) from a stranger's device.
    private static readonly PortalEvent[] Timeline =
    [
        Event(EventType.LoginSucceeded, Monday.AddDays(-20), "D-LAPTOP", "GB"),
        Event(EventType.LoginSucceeded, Monday.AddDays(-10), "D-LAPTOP", "GB"),
        Event(EventType.PasswordReset, Monday, "D-77", "RO"),
        Event(EventType.MfaEnrolled, Monday.AddMinutes(4), "D-77", "RO"),
    ];

    private static Alert MargaretsAlert()
    {
        var alert = new Alert("ACC-MARGARET", "v3");
        alert.Add(
        [
            new RuleHit("R0", 10, [Timeline[2].EventId], Monday, "First sighting on this Account: new device D-77, new country RO"),
            new RuleHit("R1", 30, [Timeline[2].EventId, Timeline[3].EventId], Monday.AddMinutes(4), "Password reset then MFA enrolment, 4 min apart; window 1h"),
        ], Bands);
        return alert;
    }

    [Fact]
    public async Task The_agent_calls_a_tool_then_returns_the_models_Finding()
    {
        var model = new ScriptedChatClient(
            new FunctionCallContent("call-1", "GetLinkedAccounts"),
            """{"verdict":"LikelyAccountTakeover","confidence":"High","summary":"A stranger took over.","recommendation":"BlockBankChanges","events":[3,4]}""");

        var result = await new Investigator(model, new MargaretsData()).InvestigateAsync(MargaretsAlert(), Ct);

        var finding = result.Finding;
        Assert.Equal((Verdict.LikelyAccountTakeover, Confidence.High, Recommendation.BlockBankChanges),
            (finding.Verdict, finding.Confidence, finding.Recommendation));
        Assert.Equal("A stranger took over.", finding.Summary);
        Assert.Equal([3, 4], finding.Events);
        // The tool's answer went back to the model on its second call.
        var toolResult = Assert.Single(model.Calls[1].SelectMany(m => m.Contents).OfType<FunctionResultContent>());
        Assert.Equal("Device D-77 is also used by ACC-DEREK (Confirmed Fraud)", toolResult.Result?.ToString());
    }

    [Fact]
    public async Task The_brief_lists_each_Rule_Hit_and_the_flagged_events_by_their_timeline_number()
    {
        var model = new ScriptedChatClient(
            """{"verdict":"Inconclusive","confidence":"Low","summary":"Not sure.","recommendation":"ContactHolder","events":[]}""");

        await new Investigator(model, new MargaretsData()).InvestigateAsync(MargaretsAlert(), Ct);

        var brief = model.Calls[0].Last(m => m.Role == ChatRole.User).Text;
        Assert.Contains("Investigate Account ACC-MARGARET. Alert Score 40, Risk Band Medium.", brief);
        Assert.Contains("- R1 (+30): Password reset then MFA enrolment, 4 min apart; window 1h. Events: #3, #4", brief);
        Assert.Contains("#3 2026-09-21 09:00 PasswordReset device D-77", brief);
        Assert.DoesNotContain("#1 ", brief);
    }

    [Fact]
    public async Task Usual_behaviour_comes_from_before_the_Alert_started()
    {
        var model = new ScriptedChatClient(
            new FunctionCallContent("call-1", "GetUsualBehaviour"),
            """{"verdict":"LikelyAccountTakeover","confidence":"High","summary":"x","recommendation":"BlockBankChanges","events":[3]}""");

        await new Investigator(model, new MargaretsData()).InvestigateAsync(MargaretsAlert(), Ct);

        var toolResult = Assert.Single(model.Calls[1].SelectMany(m => m.Contents).OfType<FunctionResultContent>());
        Assert.Equal("Before 2026-09-21 09:00: 2 events. Usual countries: GB. Usual devices: D-LAPTOP.", toolResult.Result?.ToString());
    }

    [Fact]
    public async Task The_Fake_provider_calls_a_tool_and_cites_every_flagged_event()
    {
        var result = await new Investigator(new FakeChatClient(), new MargaretsData()).InvestigateAsync(MargaretsAlert(), Ct);

        Assert.Equal(Verdict.LikelyAccountTakeover, result.Finding.Verdict);
        Assert.Equal([3, 4], result.Finding.Events);
        Assert.Equal(FakeChatClient.ModelId, result.Model);
    }

    private sealed class MargaretsData : IAccountData
    {
        public Task<IReadOnlyList<PortalEvent>> GetTimelineAsync(string accountId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<PortalEvent>>(Timeline);

        public Task<Neighbourhood> GetLinkedAccountsAsync(string accountId, CancellationToken ct) =>
            Task.FromResult(new Neighbourhood(accountId, false, [new LinkedIdentifier("Device", "D-77", [new LinkedAccount("ACC-DEREK", true)])]));
    }

    /// <summary>Answers each call with the next scripted reply, and keeps the messages of every call.</summary>
    private sealed class ScriptedChatClient(params object[] replies) : IChatClient
    {
        public List<List<ChatMessage>> Calls { get; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Calls.Add([.. messages]);
            var reply = replies[Calls.Count - 1] switch
            {
                FunctionCallContent call => new ChatMessage(ChatRole.Assistant, [call]),
                var text => new ChatMessage(ChatRole.Assistant, (string)text),
            };
            return Task.FromResult(new ChatResponse(reply));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var update in (await GetResponseAsync(messages, options, cancellationToken)).ToChatResponseUpdates())
                yield return update;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }
}
