using Vigil.Domain.Datasets;
using Vigil.Domain.Detection;
using Vigil.Simulator.Scenarios;

namespace Vigil.Evaluation.Tests;

public sealed class BlindSpotReportTests
{
    private static readonly EvaluationResult Result = new(
        "v1", new Rate(0, 0), new Rate(0, 0), new Rate(0, 0), [], [new(Persona.QuietHolder, false, 1, 0, Enum.GetValues<RiskBand>().ToDictionary(b => b, _ => 0))]);

    private static readonly EvaluationRun Run = new("run", 42, 1, "datasets/x.jsonl");

    private static readonly Scenario WaitsOutR1 = Scenario.Parse("""
        {
          "name": "waits-out-r1",
          "description": "",
          "accountId": "ACC-1",
          "label": { "kind": "Ato", "intent": "Enrols MFA 65 minutes after the reset, just outside R1's Window." },
          "baseline": { "device": "D-1", "ip": "203.0.113.1", "country": "GB" },
          "steps": [{ "after": "0m", "type": "PasswordReset" }]
        }
        """, "test.json");

    private static string Render(BlindSpotReview? review) => Report.Render(Run, Result, Result with { RulesVersion = "v2" }, blindSpots: review);

    [Fact]
    public void Each_Blind_Spot_is_listed_with_its_kind_intent_peak_band_and_the_Rules_that_did_fire()
    {
        var markdown = Render(new BlindSpotReview("v2", DryRun: 3, Fraud: 2, [new BlindSpot(WaitsOutR1, RiskBand.Low, ["R0"])]));

        Assert.Contains("## Blind Spots", markdown);
        Assert.Contains("3 Generated Scenarios dry-run against v2, 2 labelled fraud: 1 Blind Spot.", markdown);
        Assert.Contains("| waits-out-r1 | Ato | Enrols MFA 65 minutes after the reset, just outside R1's Window. | Low | R0 |", markdown);
    }

    [Fact]
    public void With_no_Blind_Spots_the_section_says_so()
    {
        var markdown = Render(new BlindSpotReview("v2", DryRun: 1, Fraud: 1, []));

        Assert.Contains("1 Generated Scenario dry-run against v2, 1 labelled fraud: 0 Blind Spots.", markdown);
        Assert.DoesNotContain("| Scenario |", markdown);
    }

    [Fact]
    public void Without_a_review_there_is_no_Blind_Spots_section()
    {
        Assert.DoesNotContain("## Blind Spots", Render(null));
    }
}
