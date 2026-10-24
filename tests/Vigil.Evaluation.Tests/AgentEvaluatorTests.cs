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
    public async Task A_failed_Investigation_is_counted_but_left_out_of_the_Finding_metrics()
    {
        var chat = new ScriptedChatClient().Fails(new HttpRequestException("503 Service Unavailable")).Writes(Finding("LikelyGenuine", "E-1"));

        var result = await RunAsync(chat, Ato, NearMiss);

        Assert.Equal(new Rate(1, 2), result.FailureRate);
        Assert.Equal(new Rate(1, 1), result.VerdictAgreement);
        Assert.Equal(new Rate(1, 1), result.EvidenceGrounding);
    }
}
