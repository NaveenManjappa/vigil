using System.Text;

namespace Vigil.Evaluation;

/// <summary>The report's Blind Spot section: fraud the Generated Scenarios found that the rules never take to High.</summary>
public static class BlindSpotReport
{
    public static void AppendSection(StringBuilder md, BlindSpotReview review)
    {
        md.AppendLine("## Blind Spots");
        md.AppendLine();
        md.AppendLine($"{Count(review.DryRun, "Generated Scenario", "Generated Scenarios")} dry-run against {review.RulesVersion}, " +
            $"{review.Fraud} labelled fraud: {Count(review.BlindSpots.Count, "Blind Spot", "Blind Spots")}. A Blind Spot is fraud " +
            "whose Account never reaches High. The dry run skips Graph Rules, as `scenario` does.");
        md.AppendLine();
        if (review.BlindSpots.Count == 0)
            return;

        md.AppendLine("| Scenario | Fraud kind | Intent | Peak band | Rules hit |");
        md.AppendLine("|---|---|---|---|---|");
        foreach (var spot in review.BlindSpots)
        {
            var label = spot.Scenario.Label!;
            var rules = spot.RulesHit.Count == 0 ? "—" : string.Join(", ", spot.RulesHit);
            md.AppendLine($"| {spot.Scenario.Name} | {label.FraudKind} | {Cell(label.Intent)} | {spot.Peak} | {rules} |");
        }
        md.AppendLine();
    }

    private static string Count(int n, string one, string many) => $"{n} {(n == 1 ? one : many)}";

    // The intent is model-written, so a stray pipe must not split the row.
    private static string Cell(string text) => text.Replace("|", "\\|");
}
