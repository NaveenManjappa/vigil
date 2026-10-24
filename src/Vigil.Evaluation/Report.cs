using System.Globalization;
using System.Text;
using Vigil.Domain.Datasets;
using Vigil.Domain.Detection;

namespace Vigil.Evaluation;

/// <summary>What identifies one evaluation run, so two reports can be compared fairly.</summary>
public sealed record EvaluationRun(string RunId, int Seed, int Holders, string DatasetPath);

public static class Report
{
    // PROJECT.md Section 10.1's expected outcome per Persona, printed beside the actual one so a gap is obvious.
    private static readonly Dictionary<Persona, string> Expected = new()
    {
        [Persona.QuietHolder] = "Low",
        [Persona.HouseMover] = "Low",
        [Persona.NewPhone] = "Medium",
        [Persona.ActiveTrader] = "Low",
        [Persona.TravellingHolder] = "High (false positive)",
        [Persona.AtoFraudster] = "High/Critical before bank change",
        [Persona.RegistrationFraudster] = "High",
        [Persona.FraudRing] = "Graph rules lift to High",
        [Persona.SlowBurnFraudster] = "Known miss",
    };

    /// <param name="off">The same Dataset scored without Graph Rules.</param>
    /// <param name="on">The same Dataset scored with Graph Rules, from a time-ordered replay into Neo4j.</param>
    public static string Render(EvaluationRun run, EvaluationResult off, EvaluationResult on)
    {
        var md = new StringBuilder();
        md.AppendLine("# Evaluation: graph off vs on");
        md.AppendLine();
        md.AppendLine("| Run | |");
        md.AppendLine("|---|---|");
        md.AppendLine($"| Run ID | `{run.RunId}` |");
        md.AppendLine($"| Seed | {run.Seed} |");
        md.AppendLine($"| Holders | {run.Holders} |");
        md.AppendLine($"| Rules Versions | {off.RulesVersion} (graph off), {on.RulesVersion} (graph on) |");
        md.AppendLine($"| Dataset | `{run.DatasetPath}` |");
        md.AppendLine();
        md.AppendLine("An Account is **flagged** when any of its Alerts reached High or above at any point. With the graph on, the " +
            "Dataset is replayed into a throwaway Neo4j in OccurredAt order, so no Graph Rule sees a link that formed after " +
            "the event being scored.");
        md.AppendLine();

        var versions = $"{off.RulesVersion} (graph off) | {on.RulesVersion} (graph on)";

        md.AppendLine("## Metrics");
        md.AppendLine();
        md.AppendLine($"| Metric | Definition | {versions} |");
        md.AppendLine("|---|---|---|---|");
        md.AppendLine($"| Detection rate | flagged fraud ÷ all fraud | {Cell(off.DetectionRate)} | {Cell(on.DetectionRate)} |");
        md.AppendLine($"| False-positive rate | flagged genuine ÷ all genuine | {Cell(off.FalsePositiveRate)} | {Cell(on.FalsePositiveRate)} |");
        md.AppendLine($"| Early-detection rate | ATO fraud flagged before its first BankDetailsChanged ÷ all ATO fraud | " +
            $"{Cell(off.EarlyDetectionRate)} | {Cell(on.EarlyDetectionRate)} |");
        md.AppendLine();

        md.AppendLine("## Per-rule precision");
        md.AppendLine();
        md.AppendLine("Hits on fraud Accounts ÷ all hits. A Rule counts once per Alert; — means the Rules Version has no such Rule.");
        md.AppendLine();
        md.AppendLine($"| Rule | Name | {versions} |");
        md.AppendLine("|---|---|---|---|");
        var offRules = off.RulePrecision.ToDictionary(r => r.RuleId);
        foreach (var rule in on.RulePrecision)
        {
            var before = offRules.TryGetValue(rule.RuleId, out var offRule) ? Cell(offRule.Precision) : "—";
            md.AppendLine($"| {rule.RuleId} | {rule.Name} | {before} | {Cell(rule.Precision)} |");
        }

        // A zero would otherwise read as G2 not working; Labels are kept out of detection on purpose.
        if (on.RulePrecision.Any(r => r.RuleId == "G2" && r.Precision.Of == 0))
        {
            md.AppendLine();
            md.AppendLine("G2 has no hits because the generated Dataset contains no Confirmed Fraud, so no Label leaks into " +
                "detection. Known-fraud proximity is demonstrated by Scenarios only: Margaret's D-77 links her to Confirmed " +
                "Fraud Account ACC-PRIYA.");
        }
        md.AppendLine();

        md.AppendLine("## By Persona");
        md.AppendLine();
        md.AppendLine("Flagged counts Accounts that reached High or above. Peak bands count Accounts by the highest band any of " +
            "their Alerts reached, as Low / Medium / High / Critical; an Account with no Alert counts as Low.");
        md.AppendLine();
        md.AppendLine($"| Persona | Label | Accounts | Flagged {off.RulesVersion} | Flagged {on.RulesVersion} | " +
            $"Peak bands {off.RulesVersion} | Peak bands {on.RulesVersion} | Expected (Section 10.1) |");
        md.AppendLine("|---|---|---|---|---|---|---|---|");
        var onPersonas = on.Personas.ToDictionary(p => p.Persona);
        foreach (var p in off.Personas)
        {
            var onPersona = onPersonas[p.Persona];
            md.AppendLine($"| {p.Persona} | {(p.IsFraud ? "Fraud" : "Genuine")} | {p.Accounts} | {p.Flagged} / {p.Accounts} | " +
                $"{onPersona.Flagged} / {onPersona.Accounts} | {PeakBands(p)} | {PeakBands(onPersona)} | {Expected[p.Persona]} |");
        }

        return md.ToString();
    }

    private static string PeakBands(PersonaOutcome p) =>
        string.Join(" / ", Enum.GetValues<RiskBand>().Select(b => p.PeakBands[b]));

    private static string Cell(Rate rate) => $"{rate.Count} / {rate.Of} ({Percent(rate)})";

    // Invariant culture, so a report reads the same whatever machine generated it.
    private static string Percent(Rate rate) =>
        rate.Value is { } v ? (v * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%" : "n/a";
}
