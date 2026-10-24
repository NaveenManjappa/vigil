using Microsoft.Extensions.AI;

namespace Vigil.Agents.Tests;

public sealed class InvestigatorTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly MargaretCase _source = new();

    private const string TakeoverFinding = """
        {"verdict":"LikelyAccountTakeover","confidence":"High","summary":"A new device took over the Account.",
         "recommendation":"BlockBankChanges","evidence":["E-1","G-2"]}
        """;

    private static FunctionCallContent Tool(string callId, string name, Dictionary<string, object?>? arguments = null) =>
        new(callId, name, arguments ?? []);

    private async Task<(Enrichment Enrichment, InvestigatorResult Result)> InvestigateAsync(
        RecordingChatClient chat, IReadOnlyList<string>? reviewerReasons = null)
    {
        var enrichment = await Enrichment.CreateAsync(MargaretCase.Alert, _source, Ct);
        var result = await new Investigator(chat, _source).InvestigateAsync(enrichment, reviewerReasons ?? [], Ct);
        return (enrichment, result);
    }

    [Fact]
    public async Task The_Investigator_calls_a_tool_then_writes_a_Finding()
    {
        var chat = new RecordingChatClient()
            .CallsTools(Tool("call-1", "QueryGraphNeighbourhood"))
            .Replies(TakeoverFinding);

        var (_, result) = await InvestigateAsync(chat);

        var finding = result.Finding;
        Assert.Equal(Verdict.LikelyAccountTakeover, finding.Verdict);
        Assert.Equal(Confidence.High, finding.Confidence);
        Assert.Equal("A new device took over the Account.", finding.Summary);
        Assert.Equal(Recommendation.BlockBankChanges, finding.Recommendation);
        Assert.Equal(["E-1", "G-2"], finding.EvidenceRefs);
        // Tokens are summed over the tool loop: both round trips are billed.
        Assert.Equal(300, result.InputTokens);
        Assert.Equal(30, result.OutputTokens);
    }

    private static RecordingChatClient CallsEveryTool() => new RecordingChatClient()
        .CallsTools(
            Tool("timeline", "GetAccountTimeline", new() { ["days"] = 30 }),
            Tool("graph", "QueryGraphNeighbourhood"),
            Tool("baseline", "GetHolderBaseline"))
        .Replies(TakeoverFinding);

    [Fact]
    public async Task Tool_results_reach_the_model_masked_and_tagged_with_Evidence_Refs()
    {
        var chat = CallsEveryTool();

        var (enrichment, _) = await InvestigateAsync(chat);

        var results = chat.ToolResults();
        var refs = enrichment.Evidence.Refs;
        string Pseudonym(string value) => enrichment.Evidence.Mask(value);

        // The bank change was not cited by any Rule Hit, so the timeline gives it a new ref, which resolves to it.
        var bankLine = Assert.Single(results["timeline"].Split('\n'), l => l.Contains("BankDetailsChanged"));
        Assert.Equal([MargaretCase.Bank.EventId], refs.Resolve(bankLine.Split(' ')[0]));
        Assert.Contains($"NewBankAccountHash={Pseudonym("BA-9f31")}", bankLine);

        // D-77's graph fact stands for Margaret's link and each other Account's link to it.
        var deviceLine = Assert.Single(results["graph"].Split('\n'), l => l.Contains(Pseudonym("D-77") + " "));
        Assert.StartsWith("G-", deviceLine);
        Assert.Equal([MargaretCase.Reset.EventId, MargaretCase.DerekReset, MargaretCase.PriyaReset.EventId], refs.Resolve(deviceLine.Split(' ')[0]));
        Assert.Contains($"{Pseudonym("ACC-PRIYA")} (Confirmed Fraud)", deviceLine);

        Assert.Contains($"Usual devices: {Pseudonym("D-MARGARET-LAPTOP")}", results["baseline"]);
    }

    [Fact]
    public async Task The_tools_are_bound_to_the_Cases_Account_and_take_no_account_argument()
    {
        var chat = new RecordingChatClient().Replies(TakeoverFinding);

        await InvestigateAsync(chat);

        var tools = chat.Calls[0].Options!.Tools!.OfType<AIFunction>().ToList();
        Assert.Equal(["GetAccountTimeline", "QueryGraphNeighbourhood", "GetHolderBaseline"], tools.Select(t => t.Name));
        var parameters = tools.SelectMany(t => t.JsonSchema.GetProperty("properties").EnumerateObject().Select(p => p.Name));
        Assert.Equal(["days"], parameters);
    }

    [Fact]
    public async Task A_timeline_request_beyond_ninety_days_is_capped()
    {
        var chat = new RecordingChatClient()
            .CallsTools(Tool("timeline", "GetAccountTimeline", new() { ["days"] = 400 }))
            .Replies(TakeoverFinding);

        await InvestigateAsync(chat);

        Assert.Equal([90], _source.TimelineDaysAsked);
    }

    [Fact]
    public async Task The_brief_gives_each_Rule_Hit_with_the_Evidence_Refs_of_its_events()
    {
        var chat = new RecordingChatClient().Replies(TakeoverFinding);

        var (enrichment, _) = await InvestigateAsync(chat);

        var brief = chat.Calls[0].Messages.Single(m => m.Role == ChatRole.User).Text;
        Assert.Contains("Score 105, Risk Band Critical", brief);
        var r1 = Assert.Single(brief.Split('\n'), l => l.StartsWith("- R1 (+30)"));
        var cited = r1[(r1.IndexOf("Evidence: ") + "Evidence: ".Length)..].Trim().Split(", ");
        Assert.Equal([[MargaretCase.Reset.EventId], [MargaretCase.Mfa.EventId]], cited.Select(enrichment.Evidence.Refs.Resolve));
    }

    [Fact]
    public async Task A_reply_that_is_not_a_Finding_fails_the_attempt()
    {
        var chat = new RecordingChatClient().Replies("I think it is probably fraud.");

        await Assert.ThrowsAnyAsync<System.Text.Json.JsonException>(() => InvestigateAsync(chat));
    }
}
