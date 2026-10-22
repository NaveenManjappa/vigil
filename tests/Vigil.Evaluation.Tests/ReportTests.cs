using Vigil.Domain.Datasets;
using Vigil.Domain.Detection;

namespace Vigil.Evaluation.Tests;

public sealed class ReportTests
{
    private static readonly IReadOnlyDictionary<RiskBand, int> NoBands =
        Enum.GetValues<RiskBand>().ToDictionary(b => b, _ => 0);

    private static readonly EvaluationResult Result = new(
        DetectionRate: new Rate(17, 30),
        FalsePositiveRate: new Rate(0, 970),
        EarlyDetectionRate: new Rate(9, 9),
        RulePrecision: [new("R1", "Recovery takeover", new Rate(9, 12)), new("R5", "Fast-track registration", new Rate(0, 0))],
        Personas:
        [
            new(Persona.QuietHolder, false, 700, 0, new Dictionary<RiskBand, int>(NoBands) { [RiskBand.Low] = 700 }),
            new(Persona.SlowBurnFraudster, true, 8, 0, new Dictionary<RiskBand, int>(NoBands) { [RiskBand.Low] = 8 }),
        ]);

    private static readonly string Markdown =
        Report.Render(Result, new EvaluationRun("20261022T203000Z-v1-seed42", Seed: 42, Holders: 1000, RulesVersion: "v1", "datasets/seed-42-holders-1000.jsonl"));

    [Fact]
    public void The_report_records_the_run_ID_seed_and_Rules_Version()
    {
        Assert.Contains("| Run ID | `20261022T203000Z-v1-seed42` |", Markdown);
        Assert.Contains("| Seed | 42 |", Markdown);
        Assert.Contains("| Rules Version | v1 |", Markdown);
    }

    [Fact]
    public void The_report_gives_all_four_metrics_as_counts_and_rates()
    {
        Assert.Contains("| Detection rate | flagged fraud ÷ all fraud | 17 / 30 | 56.7% |", Markdown);
        Assert.Contains("| False-positive rate | flagged genuine ÷ all genuine | 0 / 970 | 0.0% |", Markdown);
        Assert.Contains("| Early-detection rate | ATO fraud flagged before its first BankDetailsChanged ÷ all ATO fraud | 9 / 9 | 100.0% |", Markdown);
        Assert.Contains("| R1 | Recovery takeover | 9 / 12 | 75.0% |", Markdown);
        // A Rule with no hits has no precision, rather than a misleading 0%.
        Assert.Contains("| R5 | Fast-track registration | 0 / 0 | n/a |", Markdown);
    }

    [Fact]
    public void The_report_shows_slow_burn_as_a_known_miss()
    {
        Assert.Contains("| SlowBurnFraudster | Fraud | 8 | 0 | 8 | 8 | 0 | 0 | 0 | Known miss |", Markdown);
    }
}
