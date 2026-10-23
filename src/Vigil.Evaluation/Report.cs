using System.Globalization;
using System.Text;
using Vigil.Domain.Datasets;
using Vigil.Domain.Detection;

namespace Vigil.Evaluation;

/// <summary>What identifies one evaluation run, so two reports can be compared fairly.</summary>
public sealed record EvaluationRun(string RunId, int Seed, int Holders, string RulesVersion, string DatasetPath);

public static class Report
{
    // PROJECT.md Section 10.1's expected outcome per Persona, printed beside the actual one so a gap is obvious.
    private static readonly Dictionary<Persona, string> Expected = new()
    {
        [Persona.QuietHolder] = "Low",
        [Persona.HouseMover] = "Low",
        [Persona.NewPhone] = "Medium",
        [Persona.ActiveTrader] = "Low",
        [Persona.AtoFraudster] = "High/Critical before bank change",
        [Persona.RegistrationFraudster] = "High",
        [Persona.FraudRing] = "Medium until Stage 2 graph rules",
        [Persona.SlowBurnFraudster] = "Known miss",
    };

    public static string Render(EvaluationResult result, EvaluationRun run)
    {
        var md = new StringBuilder();
        md.AppendLine("# Rules-only evaluation");
        md.AppendLine();
        md.AppendLine("| Run | |");
        md.AppendLine("|---|---|");
        md.AppendLine($"| Run ID | `{run.RunId}` |");
        md.AppendLine($"| Seed | {run.Seed} |");
        md.AppendLine($"| Holders | {run.Holders} |");
        md.AppendLine($"| Rules Version | {run.RulesVersion} |");
        md.AppendLine($"| Dataset | `{run.DatasetPath}` |");
        md.AppendLine();
        md.AppendLine("An Account is **flagged** when any of its Alerts reached High or above at any point.");
        md.AppendLine();

        md.AppendLine("## Metrics");
        md.AppendLine();
        md.AppendLine("| Metric | Definition | Count | Rate |");
        md.AppendLine("|---|---|---|---|");
        md.AppendLine($"| Detection rate | flagged fraud ÷ all fraud | {Row(result.DetectionRate)} |");
        md.AppendLine($"| False-positive rate | flagged genuine ÷ all genuine | {Row(result.FalsePositiveRate)} |");
        md.AppendLine($"| Early-detection rate | ATO fraud flagged before its first BankDetailsChanged ÷ all ATO fraud | {Row(result.EarlyDetectionRate)} |");
        md.AppendLine();

        md.AppendLine("## Per-rule precision");
        md.AppendLine();
        md.AppendLine("Hits on fraud Accounts ÷ all hits. A Rule counts once per Alert.");
        md.AppendLine();
        md.AppendLine("| Rule | Name | Fraud hits / all hits | Precision |");
        md.AppendLine("|---|---|---|---|");
        foreach (var rule in result.RulePrecision)
            md.AppendLine($"| {rule.RuleId} | {rule.Name} | {Row(rule.Precision)} |");
        md.AppendLine();

        md.AppendLine("## By Persona");
        md.AppendLine();
        md.AppendLine("Band columns count Accounts by the highest band any of their Alerts reached.");
        md.AppendLine();
        md.AppendLine("| Persona | Label | Accounts | Flagged | Missed | Low | Medium | High | Critical | Expected (Section 10.1) |");
        md.AppendLine("|---|---|---|---|---|---|---|---|---|---|");
        foreach (var p in result.Personas)
        {
            var missed = p.IsFraud ? $"{p.Accounts - p.Flagged}" : "—";
            var bands = string.Join(" | ", Enum.GetValues<RiskBand>().Select(b => p.PeakBands[b]));
            md.AppendLine($"| {p.Persona} | {(p.IsFraud ? "Fraud" : "Genuine")} | {p.Accounts} | {p.Flagged} | {missed} | {bands} | {Expected[p.Persona]} |");
        }

        return md.ToString();
    }

    private static string Row(Rate rate) => $"{rate.Count} / {rate.Of} | {Percent(rate)}";

    // Invariant culture, so a report reads the same whatever machine generated it.
    private static string Percent(Rate rate) =>
        rate.Value is { } v ? (v * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%" : "n/a";
}
