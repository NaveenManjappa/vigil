namespace Vigil.Agents.Tests;

/// <summary>The Investigation workflow: Enrich, Investigate, a pause at Await Approval, and Act once a decision arrives.</summary>
public sealed class InvestigationWorkflowTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly Guid CaseId = Guid.NewGuid();

    private static readonly AnalystDecision Decision =
        new(DecisionOutcome.ConfirmedFraud, Recommendation.ContactHolder, "analyst-7", "Called the Holder.");

    private const string TakeoverFinding = """
        {"verdict":"LikelyAccountTakeover","confidence":"High","summary":"ACCOUNT-1 was reset from DEVICE-1 then enrolled MFA.",
         "recommendation":"BlockBankChanges","evidence":["E-2","E-3"]}
        """;

    private const string Approve = """{"approved":true,"reasons":[]}""";

    private static string Reject(string reason) => $$"""{"approved":false,"reasons":["{{reason}}"]}""";

    private readonly MargaretCase _source = new();
    private readonly InMemoryCheckpointStore _checkpoints = new();
    private readonly RecordingActStep _act = new();

    // A new instance per call, as a new request or a restarted API would build it; only the checkpoints carry over.
    private InvestigationWorkflow Workflow(RecordingChatClient chat) =>
        new(new InvestigationLoop(new Investigator(chat, _source), new Reviewer(chat)), _source, _act, _checkpoints);

    [Fact]
    public async Task An_investigated_Case_pauses_for_approval_with_its_masked_Finding_and_what_unmasks_it()
    {
        var request = await Workflow(new RecordingChatClient().Replies(TakeoverFinding).Replies(Approve))
            .InvestigateAsync(CaseId, MargaretCase.Alert, Ct);

        Assert.Equal(CaseId, request.CaseId);
        Assert.False(request.InvestigationFailed);
        Assert.False(request.ReviewFlagged);
        Assert.Equal("ACCOUNT-1 was reset from DEVICE-1 then enrolled MFA.", request.Finding!.Summary);
        Assert.Equal("ACC-MARGARET", request.PseudonymMap["ACCOUNT-1"]);
        Assert.Equal("D-77", request.PseudonymMap["DEVICE-1"]);
        Assert.Equal([MargaretCase.Reset.EventId], request.EvidenceRefs["E-2"]);
        Assert.Equal("investigator.v1+reviewer.v1", request.PromptVersion);
        Assert.Equal(2, request.Usage.Count);
        Assert.Empty(_act.Acted);
    }

    [Fact]
    public async Task A_decision_after_a_restart_resumes_from_the_checkpoint_and_reaches_Act_without_calling_the_model()
    {
        await Workflow(new RecordingChatClient().Replies(TakeoverFinding).Replies(Approve)).InvestigateAsync(CaseId, MargaretCase.Alert, Ct);

        // An empty script throws on any call, so a resume that re-ran the Investigation would fail here.
        var afterRestart = new RecordingChatClient();
        await Workflow(afterRestart).DecideAsync(CaseId, Decision, Ct);

        Assert.Equal([(CaseId, Decision)], _act.Acted);
        Assert.Empty(afterRestart.Calls);
    }

    [Fact]
    public async Task An_Investigation_whose_model_failed_still_pauses_for_approval_flagged_InvestigationFailed()
    {
        var request = await Workflow(new RecordingChatClient().Throws(new HttpRequestException("provider down")))
            .InvestigateAsync(CaseId, MargaretCase.Alert, Ct);

        Assert.True(request.InvestigationFailed);
        Assert.Null(request.Finding);
        Assert.Equal("ACC-MARGARET", request.PseudonymMap["ACCOUNT-1"]);
    }

    [Fact]
    public async Task A_Finding_rejected_after_its_retry_pauses_for_approval_with_the_Review_Flag()
    {
        var chat = new RecordingChatClient()
            .Replies(TakeoverFinding).Replies(Reject("E-3 does not show a new device."))
            .Replies(TakeoverFinding).Replies(Reject("Still unsupported."));

        var request = await Workflow(chat).InvestigateAsync(CaseId, MargaretCase.Alert, Ct);

        Assert.True(request.ReviewFlagged);
        Assert.NotNull(request.Finding);
    }

    // Each parses, because a missing property binds to null and the enum converter takes numbers, yet none could be shown.
    [Theory]
    [InlineData("""{"verdict":"LikelyAccountTakeover","confidence":"High","recommendation":"BlockBankChanges","evidence":["E-2"]}""")]
    [InlineData("""{"verdict":"LikelyAccountTakeover","confidence":"High","summary":"ACCOUNT-1 was reset.","recommendation":"BlockBankChanges"}""")]
    [InlineData("""{"verdict":7,"confidence":"High","summary":"ACCOUNT-1 was reset.","recommendation":"BlockBankChanges","evidence":["E-2"]}""")]
    public async Task An_incomplete_Finding_after_the_retry_pauses_for_approval_as_InvestigationFailed_not_flagged(string incomplete)
    {
        var chat = new RecordingChatClient().Replies(incomplete).Replies(incomplete);

        var request = await Workflow(chat).InvestigateAsync(CaseId, MargaretCase.Alert, Ct);

        Assert.True(request.InvestigationFailed);
        Assert.Null(request.Finding);
        Assert.False(request.ReviewFlagged);
        Assert.Equal(2, chat.Calls.Count);
    }

    [Fact]
    public async Task An_incomplete_Finding_retried_into_a_complete_one_goes_on_to_review()
    {
        const string noSummary = """{"verdict":"LikelyAccountTakeover","confidence":"High","recommendation":"BlockBankChanges","evidence":["E-2"]}""";
        var chat = new RecordingChatClient().Replies(noSummary).Replies(TakeoverFinding).Replies(Approve);

        var request = await Workflow(chat).InvestigateAsync(CaseId, MargaretCase.Alert, Ct);

        Assert.False(request.InvestigationFailed);
        Assert.False(request.ReviewFlagged);
        Assert.Equal(3, chat.Calls.Count);
    }

    [Fact]
    public async Task A_decision_whose_Act_failed_can_be_made_again()
    {
        await Workflow(new RecordingChatClient().Replies(TakeoverFinding).Replies(Approve)).InvestigateAsync(CaseId, MargaretCase.Alert, Ct);
        _act.FailNext = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => Workflow(new RecordingChatClient()).DecideAsync(CaseId, Decision, Ct));
        await Workflow(new RecordingChatClient()).DecideAsync(CaseId, Decision, Ct);

        Assert.Equal([(CaseId, Decision)], _act.Acted);
    }

    [Fact]
    public async Task A_decision_writes_no_checkpoint_so_a_racing_one_still_resumes_from_the_pause()
    {
        await Workflow(new RecordingChatClient().Replies(TakeoverFinding).Replies(Approve)).InvestigateAsync(CaseId, MargaretCase.Alert, Ct);
        var paused = (await _checkpoints.RetrieveIndexAsync(CaseId.ToString())).ToList();

        await Workflow(new RecordingChatClient()).DecideAsync(CaseId, Decision, Ct);
        // The racing decision reaches Act too; in the API the one-decision-per-Case constraint turns it away.
        await Workflow(new RecordingChatClient()).DecideAsync(CaseId, Decision, Ct);

        Assert.Equal(paused, await _checkpoints.RetrieveIndexAsync(CaseId.ToString()));
        Assert.Equal([(CaseId, Decision), (CaseId, Decision)], _act.Acted);
    }

    [Fact]
    public async Task A_decision_for_a_Case_that_never_paused_is_refused()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Workflow(new RecordingChatClient()).DecideAsync(CaseId, Decision, Ct));
        Assert.Empty(_act.Acted);
    }
}
