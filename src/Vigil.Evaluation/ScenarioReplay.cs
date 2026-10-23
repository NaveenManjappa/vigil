using System.Text;
using Vigil.Detection;
using Vigil.Domain.Detection;
using Vigil.Simulator.Scenarios;

namespace Vigil.Evaluation;

/// <param name="NewHits">The Rule Hits this step's event completed.</param>
/// <param name="Score">Of the Alert this step belongs to, or of the latest Alert when the step hit nothing.</param>
public sealed record ReplayRow(ScenarioStep Step, IReadOnlyList<RuleHit> NewHits, int Score, RiskBand Band);

/// <summary>
/// A dry run of one Scenario against any rule set, in memory, so "what would this rule change do to this
/// journey?" is answered in seconds, with no API, database or restart. Graph Rules are skipped, since they
/// read links to other Accounts that only the graph holds. Rows show what Vigil would have
/// concluded as each step arrived, which is how an analyst would have seen it live.
/// </summary>
public static class ScenarioReplay
{
    public static IReadOnlyList<ReplayRow> Replay(Scenario scenario, RuleSet ruleSet)
    {
        var timeline = scenario.Events();
        return scenario.Steps
            .OrderBy(s => s.Event.OccurredAt).ThenBy(s => s.Event.EventId)
            .Select(step =>
            {
                var alerts = Detector.Detect(timeline.Where(e => e.OccurredAt <= step.Event.OccurredAt).ToList(), ruleSet);

                // The completing event is always the last piece of evidence, for sequence and first-sighting rules alike.
                var newHits = alerts.SelectMany(a => a.Hits).Where(h => h.EvidenceEventIds[^1] == step.Event.EventId).ToList();
                var alert = alerts.FirstOrDefault(a => a.Hits.Any(newHits.Contains)) ?? alerts.LastOrDefault();
                return new ReplayRow(step, newHits, alert?.Score ?? 0, alert?.Band ?? RiskBand.Low);
            })
            .ToList();
    }

    public static string Render(Scenario scenario, RuleSet ruleSet, IReadOnlyList<ReplayRow> rows)
    {
        var md = new StringBuilder();
        md.AppendLine($"# {scenario.Name} (Rules Version {ruleSet.Version})");
        md.AppendLine();
        md.AppendLine(scenario.Description);
        md.AppendLine();
        if (scenario.Baseline.Count > 0)
        {
            var usual = scenario.Baseline[0];
            md.AppendLine($"{scenario.Baseline.Count} baseline logins from {usual.DeviceId} ({usual.Country}) before step 1.");
            md.AppendLine();
        }

        md.AppendLine("| Step | After | Event | As | New Rule Hits | Score | Band |");
        md.AppendLine("|---|---|---|---|---|---|---|");
        foreach (var row in rows)
        {
            var hits = row.NewHits.Count == 0 ? "—" : string.Join("; ", row.NewHits.Select(h => $"{h.RuleId} +{h.Points}: {h.Explanation}"));
            md.AppendLine($"| {row.Step.Number} | +{row.Step.After} | {row.Step.Event.Type} | {row.Step.Actor} | {hits} | {row.Score} | {row.Band} |");
        }
        md.AppendLine();

        // "Flagged" means High or above, as in the evaluation report, so the verdict reads the same way.
        var flagged = rows.FirstOrDefault(r => r.Band >= RiskBand.High);
        var peak = rows.Select(r => r.Band).DefaultIfEmpty(RiskBand.Low).Max();
        md.AppendLine(flagged is null
            ? $"Never reached High; peak {peak}."
            : $"First reached High at step {flagged.Step.Number}, {flagged.Step.Event.Type} (+{flagged.Step.After}); peak {peak}.");

        // Said outright, so a dry-run Score below the live one is not mistaken for a bug.
        var graphRules = ruleSet.Rules.Where(r => r.Kind == RuleKind.Graph).Select(r => r.Id).ToList();
        if (graphRules.Count > 0)
        {
            md.AppendLine();
            md.AppendLine($"Graph Rules {string.Join(", ", graphRules)} skipped: they need the graph, which a dry run does not have, " +
                "so the live Score can be higher.");
        }

        return md.ToString();
    }
}
