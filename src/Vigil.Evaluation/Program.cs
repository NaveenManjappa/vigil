using Vigil.Domain.Datasets;
using Vigil.Domain.Detection;
using Vigil.Evaluation;
using Vigil.Simulator;
using Vigil.Simulator.Scenarios;

// [--seed 42] [--holders 1000] [--rules-off config/rules.v1.json] [--rules-on config/rules.v2.json] [--out eval-reports]
// scenario <file> [--rules config/rules.v2.json]
// No API, no Postgres and no LLM, so it is free to run any time (PROJECT.md Section 10.3). Graph on needs Docker.
var seed = int.Parse(Option("--seed") ?? "42");
var holders = int.Parse(Option("--holders") ?? "1000");
var offPath = Option("--rules-off") ?? Path.Combine("config", "rules.v1.json");
var onPath = Option("--rules-on") ?? Path.Combine("config", "rules.v2.json");
var outFolder = Option("--out") ?? "eval-reports";
// --rules once chose the only Rules Version; ignoring it now would quietly report the defaults instead.
if (args.FirstOrDefault() != "scenario" && args.Contains("--rules"))
    throw new ArgumentException("The full evaluation takes --rules-off and --rules-on; --rules is for the scenario dry run.");

// Printed rather than written to eval-reports/: a dry run is for trying a journey or a rule change live, not for comparing runs.
if (args.FirstOrDefault() == "scenario")
{
    var scenario = Scenario.Load(args.Length > 1 ? args[1] : throw new ArgumentException("scenario needs a file path."));
    // Defaults to the API's rules, so the dry run shows the live Score less only the Graph Rules it says it skipped.
    var rules = RuleSet.Load(Option("--rules") ?? Path.Combine("config", "rules.v2.json"));
    Console.Write(ScenarioReplay.Render(scenario, rules, ScenarioReplay.Replay(scenario, rules)));
    return;
}

// Datasets are not committed, so the seeded one is regenerated when missing; the same seed gives the same file.
var datasetPath = Path.Combine("datasets", $"seed-{seed}-holders-{holders}.jsonl");
if (!File.Exists(datasetPath))
    Console.WriteLine($"Generated {GenerateCommand.Run([GenerateCommand.Name, "--seed", $"{seed}", "--holders", $"{holders}"])}");

IReadOnlyList<LabelledAccount> dataset;
using (var file = File.OpenRead(datasetPath))
    dataset = [.. Dataset.Read(file)];

var offRules = RuleSet.Load(offPath);
var onRules = RuleSet.Load(onPath);
var offResult = Evaluator.Evaluate(dataset, offRules);

IReadOnlyDictionary<string, IReadOnlyList<RuleHit>>? graphHits = null;
if (onRules.Rules.Any(r => r.Kind == RuleKind.Graph))
{
    Console.WriteLine($"Replaying {dataset.Sum(a => a.Events.Count)} events into a throwaway Neo4j...");
    await using var neo4j = await ThrowawayNeo4j.StartAsync(CancellationToken.None);
    graphHits = await GraphReplay.CollectAsync(neo4j.Driver, dataset, onRules, CancellationToken.None);
}
var onResult = Evaluator.Evaluate(dataset, onRules, graphHits);

var run = new EvaluationRun($"{DateTime.UtcNow:yyyyMMdd'T'HHmmss'Z'}-{offRules.Version}-{onRules.Version}-seed{seed}", seed, holders, datasetPath);
Directory.CreateDirectory(outFolder);
var reportPath = Path.Combine(outFolder, $"{run.RunId}.md");
File.WriteAllText(reportPath, Report.Render(run, offResult, onResult));

Console.WriteLine($"Wrote {reportPath}");
foreach (var result in new[] { offResult, onResult })
    Console.WriteLine($"{result.RulesVersion}: detection rate {result.DetectionRate.Count}/{result.DetectionRate.Of}, " +
        $"false-positive rate {result.FalsePositiveRate.Count}/{result.FalsePositiveRate.Of}, " +
        $"early-detection rate {result.EarlyDetectionRate.Count}/{result.EarlyDetectionRate.Of}");

string? Option(string name)
{
    var i = Array.IndexOf(args, name);
    if (i < 0) return null;
    if (i + 1 >= args.Length) throw new ArgumentException($"{name} needs a value.");
    return args[i + 1];
}
