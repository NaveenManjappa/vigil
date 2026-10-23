using Vigil.Domain.Datasets;
using Vigil.Domain.Detection;

namespace Vigil.Evaluation.Tests;

public sealed class ReportTests
{
    private static Dictionary<RiskBand, int> Bands(int low = 0, int medium = 0, int high = 0, int critical = 0) =>
        new() { [RiskBand.Low] = low, [RiskBand.Medium] = medium, [RiskBand.High] = high, [RiskBand.Critical] = critical };

    private static readonly EvaluationResult Off = new(
        RulesVersion: "v1",
        DetectionRate: new Rate(17, 30),
        FalsePositiveRate: new Rate(0, 970),
        EarlyDetectionRate: new Rate(9, 9),
        RulePrecision: [new("R1", "Recovery takeover", new Rate(9, 12)), new("R5", "Fast-track registration", new Rate(0, 0))],
        Personas:
        [
            new(Persona.QuietHolder, false, 700, 0, Bands(low: 700)),
            new(Persona.FraudRing, true, 5, 0, Bands(medium: 5)),
            new(Persona.SlowBurnFraudster, true, 8, 0, Bands(low: 8)),
        ]);

    private static readonly EvaluationResult On = Off with
    {
        RulesVersion = "v2",
        DetectionRate = new Rate(22, 30),
        RulePrecision = [.. Off.RulePrecision, new("G1", "Shared bank account", new Rate(5, 5)), new("G2", "Known-fraud proximity", new Rate(0, 0))],
        Personas =
        [
            Off.Personas[0],
            new(Persona.FraudRing, true, 5, 5, Bands(critical: 5)),
            Off.Personas[2],
        ],
    };

    private static readonly string Markdown =
        Report.Render(new EvaluationRun("20261023T120000Z-v1-v2-seed42", Seed: 42, Holders: 1000, "datasets/seed-42-holders-1000.jsonl"), Off, On);

    [Fact]
    public void The_report_records_the_run_ID_seed_and_both_Rules_Versions()
    {
        Assert.Contains("| Run ID | `20261023T120000Z-v1-v2-seed42` |", Markdown);
        Assert.Contains("| Seed | 42 |", Markdown);
        Assert.Contains("| Rules Versions | v1 (graph off), v2 (graph on) |", Markdown);
    }

    [Fact]
    public void The_report_gives_each_metric_as_counts_and_rates_for_both_Rules_Versions()
    {
        Assert.Contains("| Metric | Definition | v1 (graph off) | v2 (graph on) |", Markdown);
        Assert.Contains("| Detection rate | flagged fraud ÷ all fraud | 17 / 30 (56.7%) | 22 / 30 (73.3%) |", Markdown);
        Assert.Contains("| False-positive rate | flagged genuine ÷ all genuine | 0 / 970 (0.0%) | 0 / 970 (0.0%) |", Markdown);
        Assert.Contains("| Early-detection rate | ATO fraud flagged before its first BankDetailsChanged ÷ all ATO fraud | 9 / 9 (100.0%) | 9 / 9 (100.0%) |", Markdown);
    }

    [Fact]
    public void Per_rule_precision_shows_a_Graph_Rule_as_absent_from_v1()
    {
        Assert.Contains("| R1 | Recovery takeover | 9 / 12 (75.0%) | 9 / 12 (75.0%) |", Markdown);
        // A Rule with no hits has no precision, rather than a misleading 0%.
        Assert.Contains("| R5 | Fast-track registration | 0 / 0 (n/a) | 0 / 0 (n/a) |", Markdown);
        Assert.Contains("| G1 | Shared bank account | — | 5 / 5 (100.0%) |", Markdown);
    }

    [Fact]
    public void The_report_says_G2_is_demonstrated_by_Scenarios_only()
    {
        Assert.Contains("| G2 | Known-fraud proximity | — | 0 / 0 (n/a) |", Markdown);
        Assert.Contains("G2 has no hits because the generated Dataset contains no Confirmed Fraud", Markdown);
    }

    [Fact]
    public void The_report_shows_the_Ring_lifted_and_slow_burn_still_a_known_miss()
    {
        Assert.Contains("| FraudRing | Fraud | 5 | 0 / 5 | 5 / 5 | 0 / 5 / 0 / 0 | 0 / 0 / 0 / 5 | Graph rules lift to High |", Markdown);
        Assert.Contains("| SlowBurnFraudster | Fraud | 8 | 0 / 8 | 0 / 8 | 8 / 0 / 0 / 0 | 8 / 0 / 0 / 0 | Known miss |", Markdown);
    }
}
