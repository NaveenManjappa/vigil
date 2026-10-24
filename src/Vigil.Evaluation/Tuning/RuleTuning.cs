using Vigil.Domain.Detection;
using Vigil.Simulator.StandIn;

namespace Vigil.Evaluation.Tuning;

/// <summary>One Case an analyst decided, with the Rules its Alert contained.</summary>
/// <param name="Outcome">The decision's outcome as stored, one of the <see cref="DecisionValues"/> outcomes.</param>
public sealed record DecidedCase(Guid CaseId, string Outcome, string AnalystId, IReadOnlyList<string> RuleIds)
{
    public bool IsFraud => Outcome == DecisionValues.ConfirmedFraud;
}

/// <summary>A Rule's precision from Analyst Decisions; <c>Of</c> is how many decided Cases contained it.</summary>
public sealed record DecisionPrecision(string RuleId, string Name, int Points, Rate Precision);

/// <param name="Points">The Rule's points in the rules file being tuned.</param>
public sealed record WeightSuggestion(string RuleId, string Name, Rate Precision, int Points, int SuggestedPoints);

/// <summary>
/// The pure half of the Rule-Tuning Report: precision from Analyst Decisions and the weight changes it suggests.
/// It only suggests; a human writes any new rules file (ADR-0012).
/// </summary>
public static class RuleTuning
{
    public static IReadOnlyList<DecisionPrecision> Precision(IEnumerable<DecidedCase> decisions, RuleSet ruleSet)
    {
        // A Rule can fire more than once on one Alert, but the analyst judged the Case once.
        var perCase = decisions.Select(d => (d.IsFraud, RuleIds: d.RuleIds.ToHashSet())).ToList();
        return ruleSet.Rules
            .Select(r => new DecisionPrecision(r.Id, r.Name, r.Points, new Rate(
                perCase.Count(c => c.IsFraud && c.RuleIds.Contains(r.Id)),
                perCase.Count(c => c.RuleIds.Contains(r.Id)))))
            .ToList();
    }

    /// <summary>Fewer decisions than this say too little about a Rule to suggest changing it.</summary>
    public const int MinimumSupport = 5;

    /// <summary>
    /// Halves the points of each Rule wrong more often than not. Halving, not a weight proportional to precision,
    /// so the suggestion is a small, readable step a person can judge from its What-if.
    /// </summary>
    public static IReadOnlyList<WeightSuggestion> Suggest(IEnumerable<DecisionPrecision> precision) =>
        precision
            .Where(p => p.Precision.Of >= MinimumSupport && p.Precision.Value < 0.5)
            .Select(p => new WeightSuggestion(p.RuleId, p.Name, p.Precision, p.Points, HalvedToNearest5(p.Points)))
            // A Rule already at 5 points halves back to 5, which is no change worth reporting.
            .Where(s => s.SuggestedPoints < s.Points)
            .ToList();

    // Away from zero, so 25 halves to 15 rather than 10 and never to 0.
    private static int HalvedToNearest5(int points) =>
        Math.Max(5, (int)Math.Round(points / 2.0 / 5, MidpointRounding.AwayFromZero) * 5);
}
