using Vigil.Domain.Detection;
using Vigil.Simulator.Scenarios;

namespace Vigil.Evaluation.Tests;

public sealed class BlindSpotsTests
{
    private static readonly RuleSet Rules = RuleSet.Load(Path.Combine(AppContext.BaseDirectory, "config", "rules.v1.json"));

    private static Scenario Labelled(string name, string kind, string steps) => Scenario.Parse($$"""
        {
          "name": "{{name}}",
          "description": "",
          "accountId": "ACC-{{name}}",
          "label": { "kind": "{{kind}}", "intent": "test" },
          "baseline": { "device": "D-1", "ip": "203.0.113.1", "country": "GB" },
          "actors": { "fraudster": { "device": "D-77", "ip": "198.51.100.77", "country": "RO" } },
          "steps": [{{string.Join(", ", steps.Split(' ').Select(s => s.Split(':')).Select(p =>
              $$"""{ "after": "{{p[0]}}", "type": "{{p[1]}}", "as": "{{(kind == "Genuine" ? "holder" : "fraudster")}}" }"""))}}]
        }
        """, $"{name}.json");

    // R1's Window is 60 minutes, so an MFA enrolment 65 minutes after the reset scores only R0's first sighting.
    private static readonly Scenario WaitsOutR1 = Labelled("waits-out-r1", "Ato", "0m:PasswordReset 65m:MfaEnrolled");

    // Margaret's journey: R0, R1, R2 and R3 take it to Critical.
    private static readonly Scenario Caught = Labelled("caught", "Ato", "0m:PasswordReset 4m:MfaEnrolled 10m:EmailChanged 1d5h:BankDetailsChanged");

    private static readonly Scenario Quiet = Labelled("quiet", "Genuine", "0m:LoginSucceeded");

    [Fact]
    public void Fraud_that_never_reaches_High_is_a_Blind_Spot_with_its_peak_band()
    {
        var review = BlindSpots.Find([WaitsOutR1, Caught], Rules);

        var spot = Assert.Single(review.BlindSpots);
        Assert.Equal(("waits-out-r1", RiskBand.Low), (spot.Scenario.Name, spot.Peak));
        Assert.Equal(["R0"], spot.RulesHit);
    }

    [Fact]
    public void A_genuine_scenario_that_stays_Low_is_not_a_Blind_Spot()
    {
        Assert.Empty(BlindSpots.Find([Quiet], Rules).BlindSpots);
    }

    [Fact]
    public void The_review_counts_every_scenario_dry_run_and_names_the_Rules_Version()
    {
        var review = BlindSpots.Find([WaitsOutR1, Caught, Quiet], Rules);

        Assert.Equal(("v1", 3, 2), (review.RulesVersion, review.DryRun, review.Fraud));
    }
}
