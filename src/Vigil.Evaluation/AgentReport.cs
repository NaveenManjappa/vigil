using System.Globalization;
using System.Text;

namespace Vigil.Evaluation;

/// <summary>
/// The report's agent section: the metrics over the Agent Sample, then one row per Investigation. Each part is its
/// own method and each row's columns are one list, so a later measure adds a subsection or a column, not a rewrite.
/// </summary>
public static class AgentReport
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    /// <summary>The header rows naming what produced the agent numbers, so two runs can be compared knowing what changed.</summary>
    public static void AppendHeaderRows(StringBuilder md, AgentEvaluation agents)
    {
        md.AppendLine($"| Prompt Versions | {string.Join(", ", agents.PromptVersions)} |");
        md.AppendLine($"| LLM | {agents.Provider}, model {agents.Model} |");
    }

    public static void AppendSection(StringBuilder md, EvaluationRun run, AgentEvaluation agents)
    {
        md.AppendLine("## Agents");
        md.AppendLine();
        if (!agents.IsMeaningful)
        {
            md.AppendLine("> **Not meaningful:** the Fake provider writes the same scripted Finding for every Account, so these " +
                "numbers only show that the harness runs. Run with `--llm AzureOpenAI` for real ones.");
            md.AppendLine();
        }

        var nearMisses = agents.Investigations.Count(i => i.Sampled.IsNearMiss);
        var flagged = agents.Investigations.Count - nearMisses;
        md.AppendLine($"{Count(agents.Investigations.Count, "Investigation", "Investigations")}: " +
            $"{Count(flagged, "Flagged Account", "Flagged Accounts")} and {Count(nearMisses, "Near-miss", "Near-misses")}, " +
            $"drawn with seed {run.Seed}. Each ran the API's Investigate, Validate and Review loop, one retry included, on " +
            "the Dataset as it stood when its Case would have opened.");
        md.AppendLine();

        AppendMetrics(md, agents);
        AppendSample(md, agents);
    }

    private static void AppendMetrics(StringBuilder md, AgentEvaluation agents)
    {
        var investigations = agents.Investigations;
        var count = Math.Max(1, investigations.Count);

        md.AppendLine("### Metrics");
        md.AppendLine();
        md.AppendLine("| Metric | Definition | Value |");
        md.AppendLine("|---|---|---|");
        md.AppendLine($"| Evidence Grounding | first-attempt Findings whose cited Evidence Refs all exist ÷ first-attempt Findings | {Report.Cell(agents.EvidenceGrounding)} |");
        md.AppendLine($"| Verdict Agreement | Findings whose verdict fits the Label ÷ all Findings | {Report.Cell(agents.VerdictAgreement)} |");
        md.AppendLine($"| Verdict Agreement on Near-misses | Near-miss Findings saying LikelyGenuine or Inconclusive ÷ Near-miss Findings | {Report.Cell(agents.NearMissAgreement)} |");
        md.AppendLine($"| Reviewer rejection rate | Reviews that rejected the Finding ÷ all Reviews | {Report.Cell(agents.ReviewerRejectionRate)} |");
        md.AppendLine($"| Review Flag rate | Findings kept with the Review Flag ÷ all Findings | {Report.Cell(agents.ReviewFlagRate)} |");
        md.AppendLine($"| Investigations failed | Investigations that ended with no Finding ÷ all Investigations | {Report.Cell(agents.FailureRate)} |");
        md.AppendLine($"| Tokens per Investigation | mean, both agents, retries included | {Tokens((long)Math.Round(investigations.Sum(i => i.Tokens) / (double)count))} |");
        md.AppendLine($"| Cost per Investigation | mean estimated £ at the configured prices | £{Pounds(investigations.Sum(i => i.Cost) / count)} |");
        md.AppendLine($"| Seconds per Investigation | mean wall-clock time, Enrich to the last Review | {Seconds(investigations.Sum(i => i.Duration.TotalSeconds) / count)} |");
        md.AppendLine();
        md.AppendLine($"Total: {Tokens(investigations.Sum(i => i.Tokens))} tokens, £{Pounds(investigations.Sum(i => i.Cost))}.");
        md.AppendLine();
    }

    // One list, so a per-Finding measure is one more entry here rather than a second table to keep in step.
    private static readonly List<(string Header, Func<InvestigatedAccount, string> Cell)> SampleColumns =
    [
        ("Account", i => i.Sampled.Account.AccountId),
        ("Persona", i => $"{i.Sampled.Account.Label.Persona}"),
        ("Label", i => i.Sampled.Account.Label.FraudKind?.ToString() ?? "Genuine"),
        ("Sampled as", i => i.Sampled.IsNearMiss ? "Near-miss" : "Flagged"),
        ("Alert", i => $"{i.Sampled.Alert.Score} {i.Sampled.Alert.Band}"),
        ("Verdict", i => i.Outcome.Finding?.Verdict.ToString() ?? "failed"),
        ("Agrees", i => YesNo(i.Agrees)),
        ("Grounded", i => YesNo(i.Grounded)),
        // Each Finding written, in order: "invalid" when the code check rejected it before the Reviewer saw it.
        ("Reviews", i => i.Outcome.Attempts.Count == 0 ? "—"
            : string.Join(", ", i.Outcome.Attempts.Select(a => a.Approved switch { true => "approved", false => "rejected", null => "invalid" }))),
        ("Review Flag", i => i.Outcome.ReviewFlagged ? "yes" : "no"),
        ("Tokens", i => Tokens(i.Tokens)),
        ("£", i => Pounds(i.Cost)),
        ("Seconds", i => Seconds(i.Duration.TotalSeconds)),
    ];

    private static void AppendSample(StringBuilder md, AgentEvaluation agents)
    {
        md.AppendLine("### Agent Sample");
        md.AppendLine();
        md.AppendLine($"| {string.Join(" | ", SampleColumns.Select(c => c.Header))} |");
        md.AppendLine($"|{string.Concat(SampleColumns.Select(_ => "---|"))}");
        foreach (var investigation in agents.Investigations)
            md.AppendLine($"| {string.Join(" | ", SampleColumns.Select(c => c.Cell(investigation)))} |");
        md.AppendLine();
    }

    private static string YesNo(bool? value) => value switch { true => "yes", false => "no", null => "—" };

    private static string Count(int n, string one, string many) => $"{n} {(n == 1 ? one : many)}";

    private static string Tokens(long tokens) => tokens.ToString("N0", Invariant);

    // An Investigation costs a fraction of a penny, which matters on a £5–10 budget, so up to five places are kept.
    private static string Pounds(decimal pounds) => pounds.ToString("0.0####", Invariant);

    private static string Seconds(double seconds) => seconds.ToString("0.0", Invariant);
}
