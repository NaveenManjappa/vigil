using System.Text;
using static Vigil.Evaluation.Markdown;

namespace Vigil.Evaluation.Tuning;

/// <summary>What one Rule-Tuning Report was built from, so a reader can tell which decisions and Dataset it reflects.</summary>
/// <param name="RulesVersion">The rules file being tuned; its points are the "before" of every suggestion.</param>
/// <param name="ByStandIn">Decisions made by the Stand-in Analyst rather than a person.</param>
public sealed record TuningRun(string RunId, string RulesVersion, int Seed, int Holders, string DatasetPath, int Decisions, int ByStandIn);

public static class TuningReport
{
    public static string Render(TuningRun run, IReadOnlyList<DecisionPrecision> precision, IReadOnlyList<WhatIfResult> whatIfs)
    {
        var suggested = whatIfs.ToDictionary(w => w.Suggestion.RuleId, w => w.Suggestion.SuggestedPoints);
        var md = new StringBuilder();

        md.AppendLine("# Rule-Tuning Report");
        md.AppendLine();
        md.AppendLine("| Run | |");
        md.AppendLine("|---|---|");
        md.AppendLine($"| Run ID | `{run.RunId}` |");
        md.AppendLine($"| Rules Version tuned | {run.RulesVersion} |");
        md.AppendLine($"| Decisions | {run.Decisions} ({run.ByStandIn} by the Stand-in Analyst) |");
        md.AppendLine($"| What-if Dataset | `{run.DatasetPath}` (seed {run.Seed}, {run.Holders} holders) |");
        md.AppendLine();
        md.AppendLine("This report only suggests. It never writes a rules file: a person who accepts a suggestion copies the rules " +
            "file to a new version, edits it, re-runs evaluation and deploys it (ADR-0012).");
        md.AppendLine();

        md.AppendLine("## Precision from Analyst Decisions");
        md.AppendLine();
        md.AppendLine($"ConfirmedFraud Cases containing the Rule ÷ all decided Cases containing it. A Rule below 50% with at least " +
            $"{RuleTuning.MinimumSupport} decisions has its points halved to the nearest 5. Only Rules that reached a Case can " +
            "appear here; fraud the Rules miss entirely is what evaluation's detection rate is for.");
        md.AppendLine();
        md.AppendLine("| Rule | Name | Points | Decisions | Precision | Suggestion |");
        md.AppendLine("|---|---|---|---|---|---|");
        foreach (var p in precision)
        {
            var suggestion = suggested.TryGetValue(p.RuleId, out var points) ? $"Suggest {points}"
                : p.Precision.Of < RuleTuning.MinimumSupport ? "Too few decisions"
                : null;
            md.AppendLine($"| {p.RuleId} | {p.Name} | {p.Points} | {p.Precision.Count} / {p.Precision.Of} | {Percent(p.Precision)} | {suggestion}{(suggestion is null ? "" : " ")}|");
        }
        md.AppendLine();

        md.AppendLine("## What-if");
        md.AppendLine();
        if (whatIfs.Count == 0)
        {
            md.AppendLine($"No Rule is below 50% precision with at least {RuleTuning.MinimumSupport} decisions, so there is nothing to suggest.");
            return md.ToString();
        }

        md.AppendLine("Each suggestion alone, re-run over the Dataset with `Detector.Detect`, the API's own scoring run in memory. A quieter Rule can also cost " +
            "fraud caught, or caught before the bank change; that trade-off is the judgement a person makes here.");
        foreach (var w in whatIfs)
        {
            md.AppendLine();
            md.AppendLine($"### {w.Suggestion.RuleId} {w.Suggestion.Name}: {w.Suggestion.Points} → {w.Suggestion.SuggestedPoints} points");
            md.AppendLine();
            md.AppendLine($"| Metric | {w.Before.RulesVersion} | {w.After.RulesVersion} |");
            md.AppendLine("|---|---|---|");
            md.AppendLine($"| Detection rate | {Cell(w.Before.DetectionRate)} | {Cell(w.After.DetectionRate)} |");
            md.AppendLine($"| False-positive rate | {Cell(w.Before.FalsePositiveRate)} | {Cell(w.After.FalsePositiveRate)} |");
            md.AppendLine($"| Early-detection rate | {Cell(w.Before.EarlyDetectionRate)} | {Cell(w.After.EarlyDetectionRate)} |");
        }

        return md.ToString();
    }
}
