using Vigil.Agents;
using static Vigil.Evaluation.Tests.AgentSamples;

namespace Vigil.Evaluation.Tests;

public sealed class AgentReportTests
{
    private static readonly EvaluationRun Run = new("20261024T120000Z-v1-v2-seed42", Seed: 42, Holders: 1000, "datasets/seed-42-holders-1000.jsonl");

    private static readonly EvaluationResult Rules = new("v1", new Rate(0, 0), new Rate(0, 0), new Rate(0, 0), [], []);

    private static string Render(AgentEvaluation agents) => Report.Render(Run, Rules, Rules with { RulesVersion = "v2" }, agents);

    private static async Task<AgentEvaluation> FlaggedAndNearMissAsync(LlmProvider provider)
    {
        var chat = new ScriptedChatClient().Writes(Finding("LikelyAccountTakeover", "E-1")).Writes(Finding("LikelyAccountTakeover", "E-1"));
        var agents = await RunAsync(chat, Ato, NearMiss);
        return agents with { Provider = provider, Model = provider == LlmProvider.Fake ? FakeChatClient.ModelId : "gpt-4o-mini" };
    }

    [Fact]
    public async Task The_header_names_the_Prompt_Versions_provider_and_model_beside_the_run_and_Rules_Versions()
    {
        var markdown = Render(await FlaggedAndNearMissAsync(LlmProvider.AzureOpenAI));

        Assert.Contains("| Run ID | `20261024T120000Z-v1-v2-seed42` |", markdown);
        Assert.Contains("| Rules Versions | v1 (graph off), v2 (graph on) |", markdown);
        Assert.Contains("| Prompt Versions | investigator.v1+reviewer.v1 |", markdown);
        Assert.Contains("| LLM | AzureOpenAI, model gpt-4o-mini |", markdown);
    }

    [Fact]
    public async Task The_agent_metrics_are_given_as_counts_and_rates()
    {
        var markdown = Render(await FlaggedAndNearMissAsync(LlmProvider.AzureOpenAI));

        Assert.Contains("## Agents", markdown);
        Assert.Contains("2 Investigations: 1 Flagged Account and 1 Near-miss, drawn with seed 42.", markdown);
        Assert.Contains("| Evidence Grounding | first-attempt Findings whose cited Evidence Refs all exist ÷ first-attempt Findings | 2 / 2 (100.0%) |", markdown);
        Assert.Contains("| Verdict Agreement | Findings whose verdict fits the Label ÷ all Findings | 1 / 2 (50.0%) |", markdown);
        Assert.Contains("| Verdict Agreement on Near-misses | Near-miss Findings saying LikelyGenuine or Inconclusive ÷ Near-miss Findings | 0 / 1 (0.0%) |", markdown);
        Assert.Contains("| Reviewer rejection rate | Reviews that rejected the Finding ÷ all Reviews | 0 / 2 (0.0%) |", markdown);
        Assert.Contains("| Review Flag rate | Findings kept with the Review Flag ÷ all Findings | 0 / 2 (0.0%) |", markdown);
        Assert.Contains("| Tokens per Investigation | mean, both agents, retries included | 2,200 |", markdown);
        Assert.Contains("| Cost per Investigation | mean estimated £ at the configured prices | £0.00084 |", markdown);
        Assert.Contains("Total: 4,400 tokens, £0.00168.", markdown);
        Assert.DoesNotContain("not meaningful", markdown);
    }

    [Fact]
    public async Task Each_sampled_Account_gets_a_row_with_its_verdict_against_its_Label()
    {
        var markdown = Render(await FlaggedAndNearMissAsync(LlmProvider.AzureOpenAI));

        Assert.Contains("| Account | Persona | Label | Sampled as | Alert | Verdict | Agrees | Grounded | Reviews | Review Flag | Tokens | £ | Seconds |", markdown);
        Assert.Matches(@"\| ACC-ATO \| AtoFraudster \| Ato \| Flagged \| 60 High \| LikelyAccountTakeover \| yes \| yes \| approved \| no \| 2,200 \| 0\.00084 \| \d+\.\d \|", markdown);
        Assert.Matches(@"\| ACC-NEAR \| NewPhone \| Genuine \| Near-miss \| 60 High \| LikelyAccountTakeover \| no \| yes \| approved \| no \|", markdown);
    }

    [Fact]
    public async Task Fake_results_are_marked_not_meaningful()
    {
        var markdown = Render(await FlaggedAndNearMissAsync(LlmProvider.Fake));

        Assert.Contains("| LLM | Fake, model fake-model |", markdown);
        Assert.Contains("**Not meaningful:**", markdown);
    }

    [Fact]
    public void Without_agents_the_report_has_no_agent_section_or_Prompt_Versions()
    {
        var markdown = Report.Render(Run, Rules, Rules with { RulesVersion = "v2" });

        Assert.DoesNotContain("## Agents", markdown);
        Assert.DoesNotContain("Prompt Versions", markdown);
    }
}
