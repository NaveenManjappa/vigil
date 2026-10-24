using static Vigil.Evaluation.Tests.AgentSamples;

namespace Vigil.Evaluation.Tests;

public sealed class AgentEvaluatorTests
{
    [Fact]
    public async Task Evidence_Grounding_is_taken_on_the_first_attempt_even_when_the_retry_fixes_the_ref()
    {
        var chat = new ScriptedChatClient().Writes(Finding("LikelyAccountTakeover", "E-9")).Writes(Finding("LikelyAccountTakeover", "E-1"));

        var result = await RunAsync(chat, Ato);

        Assert.Equal(new Rate(0, 1), result.EvidenceGrounding);
        // The code check caught E-9, so the Reviewer only ever saw the retry, and approved it.
        Assert.Equal(new Rate(0, 1), result.ReviewerRejectionRate);
        Assert.Equal(new Rate(0, 1), result.ReviewFlagRate);
    }

    [Fact]
    public async Task Verdict_Agreement_compares_each_final_verdict_with_the_Label_and_Near_misses_are_also_shown_alone()
    {
        var chat = new ScriptedChatClient().Writes(Finding("LikelyAccountTakeover", "E-1")).Writes(Finding("LikelyAccountTakeover", "E-1"));

        var result = await RunAsync(chat, Ato, NearMiss);

        Assert.Equal(new Rate(1, 2), result.VerdictAgreement);
        Assert.Equal(new Rate(0, 1), result.NearMissAgreement);
        Assert.Equal(new Rate(2, 2), result.EvidenceGrounding);
    }

    [Fact]
    public async Task Every_Review_rejected_twice_gives_the_Review_Flag_and_counts_each_rejection()
    {
        var chat = new ScriptedChatClient().RejectsEveryReview().Writes(Finding("LikelyAccountTakeover", "E-1")).Writes(Finding("LikelyAccountTakeover", "E-1"));

        var result = await RunAsync(chat, Ato);

        Assert.Equal(new Rate(2, 2), result.ReviewerRejectionRate);
        Assert.Equal(new Rate(1, 1), result.ReviewFlagRate);
    }

    [Fact]
    public async Task Tokens_and_pounds_are_summed_over_both_agents_calls()
    {
        var chat = new ScriptedChatClient().Writes(Finding("LikelyAccountTakeover", "E-1"));

        var investigated = Assert.Single((await RunAsync(chat, Ato)).Investigations);

        // An Investigator call and a Reviewer call, each 1,000 in and 100 out: 2,000 × £0.0003/1k + 200 × £0.0012/1k.
        Assert.Equal(2_200, investigated.Tokens);
        Assert.Equal(0.00084m, investigated.Cost);
    }

    [Fact]
    public async Task The_Judge_scores_each_final_Finding_and_the_mean_is_taken_per_criterion()
    {
        var chat = new ScriptedChatClient()
            .Writes(Finding("LikelyAccountTakeover", "E-1")).Writes(Finding("LikelyGenuine", "E-1"))
            .Judges(Judgement(grounded: 5, reasoning: 4, recommendation: 2, clarity: 3))
            .Judges(Judgement(grounded: 4, reasoning: 4, recommendation: 5, clarity: 2));

        var result = await RunAsync(chat, Ato, NearMiss);

        Assert.All(result.Investigations, i => Assert.NotNull(i.Judged));
        Assert.Equal(new Rate(2, 2), result.JudgedRate);
        Assert.Equal(4.5, result.JudgeMeans.GroundedClaims);
        Assert.Equal(4.0, result.JudgeMeans.SoundReasoning);
        Assert.Equal(3.5, result.JudgeMeans.ProportionateRecommendation);
        Assert.Equal(2.5, result.JudgeMeans.Clarity);
    }

    [Fact]
    public async Task The_Judges_tokens_are_kept_apart_from_the_Investigations()
    {
        var chat = new ScriptedChatClient().Writes(Finding("LikelyAccountTakeover", "E-1"));

        var investigated = Assert.Single((await RunAsync(chat, Ato)).Investigations);

        Assert.Equal(2_200, investigated.Tokens);
        Assert.Equal(1_100, investigated.JudgeTokens);
        Assert.Equal(0.00042m, investigated.JudgeCost);
    }

    [Fact]
    public async Task A_Finding_the_Judge_fails_on_is_left_out_of_the_means_and_a_failed_Investigation_is_never_judged()
    {
        var chat = new ScriptedChatClient()
            .Fails(new HttpRequestException("503 Service Unavailable"))
            .Writes(Finding("LikelyGenuine", "E-1")).Writes(Finding("LikelyAccountTakeover", "E-1"))
            .JudgeFails(new HttpRequestException("429 Too Many Requests"))
            .Judges(Judgement(2, 2, 2, 2));
        var third = Account("ACC-THIRD", new Vigil.Domain.Datasets.Label(Vigil.Domain.Datasets.Persona.AtoFraudster, Vigil.Domain.Datasets.FraudKind.Ato), false);

        var result = await RunAsync(chat, Ato, NearMiss, third);

        Assert.Equal(2, chat.JudgeMessages.Count);
        Assert.Equal(new Rate(1, 2), result.JudgedRate);
        Assert.Equal(2.0, result.JudgeMeans.Clarity);
    }

    [Fact]
    public async Task A_failed_Investigation_is_counted_but_left_out_of_the_Finding_metrics()
    {
        var chat = new ScriptedChatClient().Fails(new HttpRequestException("503 Service Unavailable")).Writes(Finding("LikelyGenuine", "E-1"));

        var result = await RunAsync(chat, Ato, NearMiss);

        Assert.Equal(new Rate(1, 2), result.FailureRate);
        Assert.Equal(new Rate(1, 1), result.VerdictAgreement);
        Assert.Equal(new Rate(1, 1), result.EvidenceGrounding);
    }
}
