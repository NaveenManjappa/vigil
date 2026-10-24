using System.Text.Json;
using Vigil.Agents;
using static Vigil.Evaluation.Tests.AgentSamples;

namespace Vigil.Evaluation.Tests;

public sealed class JudgeTests
{
    private static async Task<Enrichment> EnrichAsync()
    {
        var (account, sampled) = Ato;
        var source = new DatasetInvestigationSource([account], sampled.Alert.Hits.Max(h => h.TriggeredAt), graph: null);
        return await Enrichment.CreateAsync(sampled.Alert, source, TestContext.Current.CancellationToken);
    }

    private static readonly Finding Cited = Vigil.Agents.Finding.FromJson(Finding("LikelyAccountTakeover", "E-1"));

    [Fact]
    public async Task Reads_a_score_and_a_reason_for_each_criterion_with_the_tokens_spent()
    {
        var chat = new ScriptedChatClient().Judges(Judgement(grounded: 5, reasoning: 4, recommendation: 3, clarity: 2));

        var result = await new Judge(chat).ScoreAsync(Cited, await EnrichAsync(), TestContext.Current.CancellationToken);

        Assert.Equal(5, result.Judgement.GroundedClaims.Score);
        Assert.Equal(4, result.Judgement.SoundReasoning.Score);
        Assert.Equal(3, result.Judgement.ProportionateRecommendation.Score);
        Assert.Equal(2, result.Judgement.Clarity.Score);
        Assert.Equal("Scored 3.", result.Judgement.ProportionateRecommendation.Reason);
        Assert.Equal(1000, result.InputTokens);
        Assert.Equal(100, result.OutputTokens);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public async Task A_score_outside_one_to_five_is_not_a_judgement(int score)
    {
        var chat = new ScriptedChatClient().Judges(Judgement(grounded: score, reasoning: 4, recommendation: 3, clarity: 2));
        var enrichment = await EnrichAsync();

        await Assert.ThrowsAsync<JsonException>(() =>
            new Judge(chat).ScoreAsync(Cited, enrichment, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_Fake_provider_gives_the_Judge_a_scripted_Judgement_so_a_free_run_judges_every_Finding()
    {
        var (account, sampled) = Ato;

        var agents = await AgentEvaluator.RunAsync([sampled], [account], graph: null, new FakeChatClient(), Prices,
            TestContext.Current.CancellationToken);

        var judged = Assert.Single(agents.Investigations).Judged;
        Assert.NotNull(judged);
        Assert.All(judged.Judgement.Scores, s => Assert.InRange(s.Score, 1, 5));
        Assert.Contains("Fake provider", judged.Judgement.Clarity.Reason);
    }

    [Fact]
    public async Task Sees_the_Finding_and_the_masked_evidence_but_no_real_identifier()
    {
        var chat = new ScriptedChatClient().Judges(Judgement(5, 5, 5, 5));

        await new Judge(chat).ScoreAsync(Cited, await EnrichAsync(), TestContext.Current.CancellationToken);

        var message = Assert.Single(chat.JudgeMessages);
        Assert.Contains("\"verdict\":\"LikelyAccountTakeover\"", message);
        Assert.Contains("E-1 ", message);
        Assert.Contains("PasswordReset", message);
        Assert.Contains("ACCOUNT-1", message);
        Assert.DoesNotContain("ACC-ATO", message);
        Assert.DoesNotContain("D-NEW", message);
    }
}
