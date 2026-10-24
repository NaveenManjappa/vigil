using Microsoft.Extensions.AI;

namespace Vigil.Agents.Tests;

public sealed class InvestigationLoopTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly MargaretCase _source = new();

    private const string TakeoverFinding = """
        {"verdict":"LikelyAccountTakeover","confidence":"High","summary":"A new device reset the password then enrolled MFA.",
         "recommendation":"BlockBankChanges","evidence":["E-2","E-3"]}
        """;

    private const string ContactFinding = """
        {"verdict":"LikelyAccountTakeover","confidence":"Medium","summary":"A new device from RO reset the password.",
         "recommendation":"ContactHolder","evidence":["E-2"]}
        """;

    private const string Approve = """{"approved":true,"reasons":[]}""";

    private static string Reject(string reason) => $$"""{"approved":false,"reasons":["{{reason}}"]}""";

    private async Task<(Enrichment Enrichment, InvestigationOutcome Outcome)> RunAsync(RecordingChatClient chat)
    {
        var enrichment = await Enrichment.CreateAsync(MargaretCase.Alert, _source, Ct);
        var loop = new InvestigationLoop(new Investigator(chat, _source), new Reviewer(chat));
        return (enrichment, await loop.RunAsync(enrichment, Ct));
    }

    private static bool IsReviewer((IReadOnlyList<ChatMessage> Messages, ChatOptions? Options) call) =>
        call.Options?.Instructions == Prompts.Load(Reviewer.PromptVersion);

    private static string UserText((IReadOnlyList<ChatMessage> Messages, ChatOptions? Options) call) =>
        string.Join('\n', call.Messages.Where(m => m.Role == ChatRole.User).Select(m => m.Text));

    [Fact]
    public async Task An_approved_Finding_goes_to_the_analyst_unflagged_with_both_agents_tokens()
    {
        var chat = new RecordingChatClient().Replies(TakeoverFinding).Replies(Approve);

        var (_, outcome) = await RunAsync(chat);

        Assert.False(outcome.InvestigationFailed);
        Assert.False(outcome.ReviewFlagged);
        Assert.Equal(Recommendation.BlockBankChanges, outcome.Finding!.Recommendation);
        Assert.Equal(
            [new AgentUsage(Investigator.AgentId, Investigator.PromptVersion, 200, 20), new AgentUsage(Reviewer.AgentId, Reviewer.PromptVersion, 200, 20)],
            outcome.Usage);
    }

    [Fact]
    public async Task A_rejected_Finding_is_retried_once_with_the_Reviewers_reasons_and_the_retry_is_kept()
    {
        var chat = new RecordingChatClient()
            .Replies(TakeoverFinding).Replies(Reject("E-3 does not show a new device."))
            .Replies(ContactFinding).Replies(Approve);

        var (_, outcome) = await RunAsync(chat);

        Assert.False(outcome.ReviewFlagged);
        Assert.Equal(Recommendation.ContactHolder, outcome.Finding!.Recommendation);
        Assert.Contains("- E-3 does not show a new device.", UserText(chat.Calls[2]));
        Assert.Equal(4, outcome.Usage.Count);
    }

    [Fact]
    public async Task A_Finding_rejected_after_its_retry_reaches_the_analyst_with_the_Review_Flag()
    {
        var chat = new RecordingChatClient()
            .Replies(TakeoverFinding).Replies(Reject("E-3 does not show a new device."))
            .Replies(ContactFinding).Replies(Reject("Nothing shows RO is unusual for the Holder."));

        var (_, outcome) = await RunAsync(chat);

        Assert.True(outcome.ReviewFlagged);
        Assert.False(outcome.InvestigationFailed);
        Assert.Equal(Recommendation.ContactHolder, outcome.Finding!.Recommendation);
        Assert.Equal(["Nothing shows RO is unusual for the Holder."], outcome.RejectionReasons);
        Assert.Equal(4, chat.Calls.Count);
    }

    [Fact]
    public async Task A_Finding_citing_an_unknown_ref_is_rejected_in_code_without_a_Reviewer_call()
    {
        var hallucinated = TakeoverFinding.Replace("\"E-3\"", "\"E-42\"");
        var chat = new RecordingChatClient().Replies(hallucinated).Replies(TakeoverFinding).Replies(Approve);

        var (_, outcome) = await RunAsync(chat);

        Assert.False(IsReviewer(chat.Calls[0]));
        Assert.False(IsReviewer(chat.Calls[1]));
        Assert.Contains("E-42", UserText(chat.Calls[1]));
        Assert.True(IsReviewer(chat.Calls[2]));
        Assert.False(outcome.ReviewFlagged);
    }

    [Fact]
    public async Task Each_Finding_written_is_kept_as_an_Attempt_with_its_unknown_refs_and_the_Reviewers_answer()
    {
        var hallucinated = TakeoverFinding.Replace("\"E-3\"", "\"E-42\"");
        var chat = new RecordingChatClient()
            .Replies(hallucinated)
            .Replies(TakeoverFinding).Replies(Reject("E-3 does not show a new device."));

        var (_, outcome) = await RunAsync(chat);

        Assert.Collection(outcome.Attempts,
            first =>
            {
                Assert.Equal(["E-2", "E-42"], first.Finding.EvidenceRefs);
                Assert.Equal(["E-42"], first.UnknownRefs);
                Assert.Null(first.Approved);
            },
            second =>
            {
                Assert.Empty(second.UnknownRefs);
                Assert.False(second.Approved);
            });
    }

    [Fact]
    public async Task Two_Findings_failing_validation_are_flagged_and_the_Reviewer_is_never_called()
    {
        var hallucinated = TakeoverFinding.Replace("\"E-3\"", "\"E-42\"");
        var chat = new RecordingChatClient().Replies(hallucinated).Replies(hallucinated);

        var (_, outcome) = await RunAsync(chat);

        Assert.DoesNotContain(chat.Calls, IsReviewer);
        Assert.True(outcome.ReviewFlagged);
        Assert.Contains(outcome.RejectionReasons, r => r.Contains("E-42"));
    }

    [Fact]
    public async Task A_malformed_reply_is_retried_and_a_second_one_fails_the_Investigation()
    {
        var chat = new RecordingChatClient().Replies("I think it is probably fraud.").Replies("Still fraud, I think.");

        var (_, outcome) = await RunAsync(chat);

        Assert.True(outcome.InvestigationFailed);
        Assert.Null(outcome.Finding);
        Assert.DoesNotContain(chat.Calls, IsReviewer);
        Assert.Contains("JSON", UserText(chat.Calls[1]));
        Assert.NotNull(outcome.Failure);
    }

    [Fact]
    public async Task A_malformed_reply_then_a_good_Finding_goes_on_to_review()
    {
        var chat = new RecordingChatClient().Replies("I think it is probably fraud.").Replies(TakeoverFinding).Replies(Approve);

        var (_, outcome) = await RunAsync(chat);

        Assert.False(outcome.InvestigationFailed);
        Assert.False(outcome.ReviewFlagged);
    }

    [Fact]
    public async Task An_LLM_failure_gives_InvestigationFailed_with_no_Finding()
    {
        var chat = new RecordingChatClient().Throws(new HttpRequestException("503 Service Unavailable"));

        var (_, outcome) = await RunAsync(chat);

        Assert.True(outcome.InvestigationFailed);
        Assert.Null(outcome.Finding);
        Assert.Contains("503", outcome.Failure);
    }

    [Fact]
    public async Task A_Reviewer_failure_gives_InvestigationFailed_but_keeps_the_Investigators_tokens()
    {
        var chat = new RecordingChatClient().Replies(TakeoverFinding).Throws(new TimeoutException("The model timed out."));

        var (_, outcome) = await RunAsync(chat);

        Assert.True(outcome.InvestigationFailed);
        Assert.Equal([new AgentUsage(Investigator.AgentId, Investigator.PromptVersion, 200, 20)], outcome.Usage);
    }

    [Fact]
    public async Task A_provider_timeout_fails_the_Investigation_but_shutting_down_cancels_it()
    {
        var timedOut = new RecordingChatClient().Throws(new TaskCanceledException("HttpClient.Timeout elapsed"));
        Assert.True((await RunAsync(timedOut)).Outcome.InvestigationFailed);

        using var shutdown = new CancellationTokenSource();
        await shutdown.CancelAsync();
        var enrichment = await Enrichment.CreateAsync(MargaretCase.Alert, _source, Ct);
        var loop = new InvestigationLoop(new Investigator(new RecordingChatClient().Replies(TakeoverFinding), _source), new Reviewer(new RecordingChatClient()));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => loop.RunAsync(enrichment, shutdown.Token));
    }

    [Fact]
    public async Task No_raw_identifier_reaches_either_agent_in_instructions_messages_or_tool_results()
    {
        // The model echoing raw values is the worst case: a summary and a rejection reason that name D-77 outright.
        var leakyFinding = TakeoverFinding.Replace("A new device", "Device D-77");
        var chat = new RecordingChatClient()
            .CallsTools(
                new FunctionCallContent("timeline", "GetAccountTimeline", new Dictionary<string, object?> { ["days"] = 30 }),
                new FunctionCallContent("graph", "QueryGraphNeighbourhood", new Dictionary<string, object?>()),
                new FunctionCallContent("baseline", "GetHolderBaseline", new Dictionary<string, object?>()))
            .Replies(leakyFinding).Replies(Reject("The summary does not explain D-77."))
            .Replies(leakyFinding).Replies(Approve);

        var (enrichment, _) = await RunAsync(chat);

        var sent = chat.EverythingSent().ToList();
        Assert.Contains(sent, text => text.Contains("fraud investigator"));
        Assert.Contains(sent, text => text.Contains("reviewer of a fraud investigator"));
        Assert.Contains(sent, text => text.Contains($"The summary does not explain {enrichment.Evidence.Mask("D-77")}."));
        Assert.Contains(sent, text => text.Contains($"Device {enrichment.Evidence.Mask("D-77")} reset the password"));
        // Amount is on the allow-list: the agents reason on it, so it is the one attribute shown as it is.
        Assert.Contains(sent, text => text.Contains("Amount=25000.00"));
        foreach (var raw in MargaretCase.RawIdentifiers)
            Assert.DoesNotContain(sent, text => text.Contains(raw));
    }
}
