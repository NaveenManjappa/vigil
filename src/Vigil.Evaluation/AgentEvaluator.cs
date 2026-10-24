using System.Diagnostics;
using Microsoft.Extensions.AI;
using Vigil.Agents;
using Vigil.Domain.Datasets;
using Vigil.Domain.Graph;

namespace Vigil.Evaluation;

/// <summary>
/// One Account of the Agent Sample after its Investigation. The Enrichment is kept because it holds the masked
/// evidence and the Evidence Refs the Finding cites, which is what anything scoring the Finding later must read.
/// </summary>
/// <param name="Tokens">The Investigator's and Reviewer's; the Judge's are kept apart, since production never spends them.</param>
/// <param name="Judged">Null when there was no Finding to judge or the Judge failed on it.</param>
public sealed record InvestigatedAccount(
    SampledAccount Sampled, Enrichment Enrichment, InvestigationOutcome Outcome, long Tokens, decimal Cost, TimeSpan Duration,
    JudgeResult? Judged = null, decimal JudgeCost = 0)
{
    public long JudgeTokens => Judged is null ? 0 : Judged.InputTokens + Judged.OutputTokens;

    /// <summary>Null when the Investigator never wrote a Finding.</summary>
    public bool? Grounded => Outcome.Attempts.FirstOrDefault() is { } first ? first.UnknownRefs.Count == 0 : null;

    /// <summary>Null when the Investigation failed, so there is no verdict to agree.</summary>
    public bool? Agrees => Outcome.Finding is { } finding ? VerdictAgreement.Fits(finding.Verdict, Sampled.Account.Label) : null;
}

/// <summary>The agent half of an evaluation run: who answered, and what each sampled Investigation produced.</summary>
/// <param name="PromptVersions">Every Prompt Version the run used, for the report header.</param>
public sealed record AgentEvaluation(
    LlmProvider Provider, string Model, IReadOnlyList<string> PromptVersions, IReadOnlyList<InvestigatedAccount> Investigations)
{
    /// <summary>The Fake provider writes the same Finding every time, so its numbers only show the harness runs.</summary>
    public bool IsMeaningful => Provider != LlmProvider.Fake;

    private IEnumerable<InvestigatedAccount> WithFinding => Investigations.Where(i => !i.Outcome.InvestigationFailed);

    public Rate FailureRate => new(Investigations.Count(i => i.Outcome.InvestigationFailed), Investigations.Count);

    public Rate EvidenceGrounding => Of(Investigations.Select(i => i.Grounded));

    /// <summary>Of every Review the Reviewer gave, retries included; a Finding validation rejected never reached it.</summary>
    public Rate ReviewerRejectionRate
    {
        get
        {
            var reviews = Investigations.SelectMany(i => i.Outcome.Attempts).Where(a => a.Approved is not null).ToList();
            return new(reviews.Count(a => a.Approved == false), reviews.Count);
        }
    }

    public Rate ReviewFlagRate => new(WithFinding.Count(i => i.Outcome.ReviewFlagged), WithFinding.Count());

    public Rate VerdictAgreement => Of(Investigations.Select(i => i.Agrees));

    /// <summary>Shown alone because the Flagged Accounts outnumber them: can the agents clear a false alarm?</summary>
    public Rate NearMissAgreement => Of(Investigations.Where(i => i.Sampled.IsNearMiss).Select(i => i.Agrees));

    /// <summary>Of the Findings, those the Judge returned a full Judgement on.</summary>
    public Rate JudgedRate => new(WithFinding.Count(i => i.Judged is not null), WithFinding.Count());

    public JudgeMeans JudgeMeans
    {
        get
        {
            var judgements = Investigations.Select(i => i.Judged?.Judgement).OfType<Judgement>().ToList();
            double? Mean(Func<Judgement, JudgeScore> criterion) => judgements.Count == 0 ? null : judgements.Average(j => criterion(j).Score);
            return new(Mean(j => j.GroundedClaims), Mean(j => j.SoundReasoning), Mean(j => j.ProportionateRecommendation), Mean(j => j.Clarity));
        }
    }

    /// <summary>One line for the console, so a run's headline numbers show without opening the report.</summary>
    public string Summary() =>
        $"agents ({Provider}{(IsMeaningful ? "" : ", not meaningful")}): " +
        $"Evidence Grounding {EvidenceGrounding.Count}/{EvidenceGrounding.Of}, " +
        $"Verdict Agreement {VerdictAgreement.Count}/{VerdictAgreement.Of}, " +
        $"{Investigations.Sum(i => i.Tokens)} tokens, £{Investigations.Sum(i => i.Cost)}; " +
        $"Judge {JudgedRate.Count}/{JudgedRate.Of} judged, {Investigations.Sum(i => i.JudgeTokens)} tokens";

    private static Rate Of(IEnumerable<bool?> answers)
    {
        var known = answers.OfType<bool>().ToList();
        return new(known.Count(a => a), known.Count);
    }
}

/// <summary>The mean Judge score per criterion over the judged Findings; null when none was judged.</summary>
public sealed record JudgeMeans(double? GroundedClaims, double? SoundReasoning, double? ProportionateRecommendation, double? Clarity);

/// <summary>
/// Runs the same <see cref="InvestigationLoop"/> the API runs on each Account of the Agent Sample, in process, one
/// at a time, so the token log reads in order and a real run stays within its budget (ADR-0016).
/// </summary>
public static class AgentEvaluator
{
    /// <param name="graph">Reads a Neighbourhood from the replayed graph; null when the run has no Graph Rules.</param>
    public static async Task<AgentEvaluation> RunAsync(
        IReadOnlyList<SampledAccount> sample, IReadOnlyCollection<LabelledAccount> dataset,
        Func<string, CancellationToken, Task<Neighbourhood>>? graph, IChatClient chat, LlmOptions llm, CancellationToken ct)
    {
        var judge = new Judge(chat);
        List<InvestigatedAccount> investigations = [];
        foreach (var sampled in sample)
        {
            // In production the Investigation starts once the Alert's latest Rule Hit has raised it.
            var source = new DatasetInvestigationSource(dataset, sampled.Alert.Hits.Max(h => h.TriggeredAt), graph);
            var clock = Stopwatch.StartNew();
            var enrichment = await Enrichment.CreateAsync(sampled.Alert, source, ct);
            var outcome = await new InvestigationLoop(new Investigator(chat, source), new Reviewer(chat)).RunAsync(enrichment, ct);
            clock.Stop();

            var input = outcome.Usage.Sum(u => u.InputTokens);
            var output = outcome.Usage.Sum(u => u.OutputTokens);
            // After the clock stops: judging is evaluation's cost, not the Investigation's.
            var judged = outcome.Finding is { } finding ? await JudgeAsync(judge, finding, enrichment, ct) : null;
            investigations.Add(new(sampled, enrichment, outcome, input + output, llm.CostOf(input, output), clock.Elapsed,
                judged, judged is null ? 0 : llm.CostOf(judged.InputTokens, judged.OutputTokens)));
        }

        var model = llm.Provider == LlmProvider.Fake ? FakeChatClient.ModelId : llm.Deployment ?? "unknown";
        return new AgentEvaluation(llm.Provider, model, [InvestigationWorkflow.PromptVersion, Judge.PromptVersion], investigations);
    }

    // One unjudged Finding should not cost the whole run its report; it is counted as unjudged instead.
    private static async Task<JudgeResult?> JudgeAsync(Judge judge, Finding finding, Enrichment enrichment, CancellationToken ct)
    {
        try
        {
            return await judge.ScoreAsync(finding, enrichment, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            Console.Error.WriteLine($"The Judge failed on {enrichment.AccountId}: {e.Message}");
            return null;
        }
    }
}
