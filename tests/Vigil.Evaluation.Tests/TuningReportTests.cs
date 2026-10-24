using Vigil.Evaluation.Tuning;

namespace Vigil.Evaluation.Tests;

public sealed class TuningReportTests
{
    private static EvaluationResult Result(string version, Rate detection, Rate fpr, Rate early) => new(version, detection, fpr, early, [], []);

    private static readonly TuningRun Run = new("20261024T120000Z-v2-seed42", "v2", 42, 1000, "datasets/seed-42-holders-1000.jsonl", Decisions: 40, ByStandIn: 38);

    [Fact]
    public void The_report_shows_each_Rules_precision_and_each_suggestions_trade_off()
    {
        var r2 = new WeightSuggestion("R2", "Contact hijack", new Rate(9, 29), 20, 10);
        DecisionPrecision[] precision =
        [
            new("R1", "Recovery takeover", 30, new Rate(20, 22)),
            new("R2", "Contact hijack", 20, new Rate(9, 29)),
            new("R6", "Register-and-sell", 30, new Rate(0, 0)),
        ];
        var whatIf = new WhatIfResult(r2,
            Result("v2", new Rate(25, 30), new Rate(20, 970), new Rate(18, 20)),
            Result("v2 with R2 at 10", new Rate(24, 30), new Rate(2, 970), new Rate(10, 20)));

        var md = TuningReport.Render(Run, precision, [whatIf]);

        Assert.Contains("| Decisions | 40 (38 by the Stand-in Analyst) |", md);
        Assert.Contains("| R2 | Contact hijack | 20 | 9 / 29 | 31.0% | Suggest 10 |", md);
        Assert.Contains("| R1 | Recovery takeover | 30 | 20 / 22 | 90.9% | |", md);
        Assert.Contains("| R6 | Register-and-sell | 30 | 0 / 0 | n/a | Too few decisions |", md);
        Assert.Contains("### R2 Contact hijack: 20 → 10 points", md);
        Assert.Contains("| Detection rate | 25 / 30 (83.3%) | 24 / 30 (80.0%) |", md);
        Assert.Contains("| False-positive rate | 20 / 970 (2.1%) | 2 / 970 (0.2%) |", md);
        Assert.Contains("| Early-detection rate | 18 / 20 (90.0%) | 10 / 20 (50.0%) |", md);
        Assert.Contains("never writes a rules file", md);
    }

    [Fact]
    public void A_report_with_no_suggestions_says_so()
    {
        var md = TuningReport.Render(Run, [new("R1", "Recovery takeover", 30, new Rate(20, 22))], []);

        Assert.Contains("No Rule is below 50% precision with at least 5 decisions", md);
    }
}
