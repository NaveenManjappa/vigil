using Vigil.Domain.Datasets;
using Vigil.Domain.Detection;
using Vigil.Simulator;
using Vigil.Simulator.StandIn;

namespace Vigil.Evaluation.Tuning;

/// <summary>
/// <c>tune --connection &lt;postgres&gt; [--rules config/rules.v2.json] [--seed 42] [--holders 1000] [--out eval-reports]</c>:
/// writes a Rule-Tuning Report from the API's Analyst Decisions. The seed and holders name the Dataset the What-ifs
/// re-run, which should be the one the Stand-in Analyst decided.
/// </summary>
public static class TuneCommand
{
    public const string Name = "tune";

    /// <returns>The report's path.</returns>
    public static async Task<string> RunAsync(string[] args, CancellationToken ct = default)
    {
        var connection = Option(args, "--connection") ?? throw new ArgumentException("tune needs --connection, the API's Postgres connection string.");
        // Defaults to the API's rules, the version the decided Alerts were scored with.
        var rulesPath = Option(args, "--rules") ?? Path.Combine("config", "rules.v2.json");
        var seed = int.Parse(Option(args, "--seed") ?? "42");
        var holders = int.Parse(Option(args, "--holders") ?? "1000");
        var outFolder = Option(args, "--out") ?? "eval-reports";

        var ruleSet = RuleSet.Load(rulesPath);
        var decisions = await DecisionReader.ReadAsync(connection, ct);
        var precision = RuleTuning.Precision(decisions, ruleSet);
        var suggestions = RuleTuning.Suggest(precision);

        var datasetPath = Path.Combine("datasets", $"seed-{seed}-holders-{holders}.jsonl");
        if (!File.Exists(datasetPath))
            Console.WriteLine($"Generated {GenerateCommand.Run([GenerateCommand.Name, "--seed", $"{seed}", "--holders", $"{holders}"])}");
        IReadOnlyList<LabelledAccount> dataset;
        using (var file = File.OpenRead(datasetPath))
            dataset = [.. Dataset.Read(file)];

        // Replayed once and shared by every What-if: which Accounts a Graph Rule links does not depend on points.
        IReadOnlyDictionary<string, IReadOnlyList<RuleHit>>? graphHits = null;
        if (suggestions.Count > 0 && ruleSet.Rules.Any(r => r.Kind == RuleKind.Graph))
        {
            Console.WriteLine($"Replaying {dataset.Sum(a => a.Events.Count)} events into a throwaway Neo4j...");
            await using var neo4j = await ThrowawayNeo4j.StartAsync(ct);
            graphHits = await GraphReplay.CollectAsync(neo4j.Driver, dataset, ruleSet, ct);
        }
        var whatIfs = suggestions.Select(s => WhatIf.Measure(dataset, ruleSet, s, graphHits)).ToList();

        var run = new TuningRun($"{DateTime.UtcNow:yyyyMMdd'T'HHmmss'Z'}-{ruleSet.Version}-seed{seed}", ruleSet.Version, seed, holders, datasetPath,
            decisions.Count, decisions.Count(d => d.AnalystId == StandInAnalyst.AnalystId));
        Directory.CreateDirectory(outFolder);
        var reportPath = Path.Combine(outFolder, $"rule-tuning-{run.RunId}.md");
        await File.WriteAllTextAsync(reportPath, TuningReport.Render(run, precision, whatIfs), ct);

        foreach (var w in whatIfs)
            Console.WriteLine($"{w.Suggestion.RuleId} {w.Suggestion.Points} → {w.Suggestion.SuggestedPoints} " +
                $"(precision {w.Suggestion.Precision.Count}/{w.Suggestion.Precision.Of}): " +
                $"false-positive rate {w.Before.FalsePositiveRate.Count} → {w.After.FalsePositiveRate.Count}, " +
                $"early-detection rate {w.Before.EarlyDetectionRate.Count} → {w.After.EarlyDetectionRate.Count} of {w.After.EarlyDetectionRate.Of}");
        return reportPath;
    }

    private static string? Option(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        if (i < 0) return null;
        if (i + 1 >= args.Length) throw new ArgumentException($"{name} needs a value.");
        return args[i + 1];
    }
}
