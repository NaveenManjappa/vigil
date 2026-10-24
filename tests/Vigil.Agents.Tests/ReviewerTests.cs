using Microsoft.Extensions.AI;

namespace Vigil.Agents.Tests;

public sealed class ReviewerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly Finding Takeover = new(Verdict.LikelyAccountTakeover, Confidence.High,
        "A password reset then MFA enrolment from a new device.", Recommendation.BlockBankChanges, ["E-2", "E-3"]);

    private static async Task<(Enrichment Enrichment, ReviewerResult Result)> ReviewAsync(RecordingChatClient chat, Finding finding)
    {
        var enrichment = await Enrichment.CreateAsync(MargaretCase.Alert, new MargaretCase(), Ct);
        var result = await new Reviewer(chat).ReviewAsync(finding, enrichment.Evidence, Ct);
        return (enrichment, result);
    }

    [Fact]
    public async Task The_Reviewers_verdict_and_reasons_come_back_with_its_tokens()
    {
        var chat = new RecordingChatClient().Replies("""{"approved":false,"reasons":["E-3 does not show a new device."]}""");

        var (_, result) = await ReviewAsync(chat, Takeover);

        Assert.False(result.Review.Approved);
        Assert.Equal(["E-3 does not show a new device."], result.Review.Reasons);
        Assert.Equal((200, 20), (result.InputTokens, result.OutputTokens));
    }

    [Fact]
    public async Task The_Reviewer_sees_the_Finding_and_the_evidence_lines_of_only_the_refs_it_cites()
    {
        var chat = new RecordingChatClient().Replies("""{"approved":true,"reasons":[]}""");

        var (enrichment, _) = await ReviewAsync(chat, Takeover);

        var call = Assert.Single(chat.Calls);
        var message = call.Messages.Single(m => m.Role == ChatRole.User).Text;
        var briefLines = enrichment.Brief.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        string EvidenceLine(string evidenceRef) => briefLines.Single(l => l.StartsWith(evidenceRef + " "));

        Assert.Contains(Takeover.Summary, message);
        Assert.Contains("LikelyAccountTakeover", message);
        Assert.Contains(EvidenceLine("E-2"), message);
        Assert.Contains(EvidenceLine("E-3"), message);
        Assert.DoesNotContain(EvidenceLine("E-4"), message);
        Assert.DoesNotContain(EvidenceLine("E-1"), message);
    }

    [Fact]
    public async Task The_Reviewer_has_its_own_prompt_and_no_tools()
    {
        var chat = new RecordingChatClient().Replies("""{"approved":true,"reasons":[]}""");

        await ReviewAsync(chat, Takeover);

        var options = Assert.Single(chat.Calls).Options!;
        Assert.Equal(Prompts.Load("reviewer.v1"), options.Instructions);
        Assert.True(options.Tools is null or { Count: 0 });
    }

    [Fact]
    public async Task A_reply_that_is_not_a_review_fails_the_review()
    {
        var chat = new RecordingChatClient().Replies("Looks fine to me.");

        await Assert.ThrowsAnyAsync<System.Text.Json.JsonException>(() => ReviewAsync(chat, Takeover));
    }
}
