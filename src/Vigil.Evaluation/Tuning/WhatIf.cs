using Vigil.Domain.Datasets;
using Vigil.Domain.Detection;

namespace Vigil.Evaluation.Tuning;

/// <summary>One suggestion's What-if: the Dataset scored with the rules file as it is, then with only that change.</summary>
public sealed record WhatIfResult(WeightSuggestion Suggestion, EvaluationResult Before, EvaluationResult After);

public static class WhatIf
{
    /// <param name="graphHits">Each Account's graph Rule Hits, collected once with the unchanged rules; none when the graph is off.</param>
    public static WhatIfResult Measure(
        IReadOnlyList<LabelledAccount> dataset, RuleSet ruleSet, WeightSuggestion suggestion,
        IReadOnlyDictionary<string, IReadOnlyList<RuleHit>>? graphHits = null)
    {
        var changed = ruleSet with
        {
            Version = $"{ruleSet.Version} with {suggestion.RuleId} at {suggestion.SuggestedPoints}",
            Rules = [.. ruleSet.Rules.Select(r => r.Id == suggestion.RuleId ? r with { Points = suggestion.SuggestedPoints } : r)],
        };

        // Graph hits arrive already carrying their points, so a graph Rule's change is applied to them here;
        // which Accounts they link does not depend on points, so the one replay serves both runs.
        var changedGraphHits = graphHits?.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<RuleHit>)[.. kv.Value.Select(h => h.RuleId == suggestion.RuleId ? h with { Points = suggestion.SuggestedPoints } : h)]);

        return new WhatIfResult(suggestion, Evaluator.Evaluate(dataset, ruleSet, graphHits), Evaluator.Evaluate(dataset, changed, changedGraphHits));
    }
}
