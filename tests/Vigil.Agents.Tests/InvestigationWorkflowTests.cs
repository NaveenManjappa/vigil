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
        Assert.False(request.ReviewFlag);
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

        Assert.True(request.ReviewFlag);
        Assert.NotNull(request.Finding);
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
    public async Task A_decision_for_a_Case_that_never_paused_is_refused()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Workflow(new RecordingChatClient()).DecideAsync(CaseId, Decision, Ct));
        Assert.Empty(_act.Acted);
    }
}
