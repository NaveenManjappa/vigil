using Vigil.Domain.Datasets;
using Vigil.Domain.Detection;
using Vigil.Evaluation;
using Vigil.Simulator;
using Vigil.Simulator.Scenarios;

// [--seed 42] [--holders 1000] [--rules config/rules.v1.json] [--out eval-reports]
// scenario <file> [--rules config/rules.v1.json]
// Rules-only and in memory: no API, no database, no LLM, so it is free to run any time (PROJECT.md Section 10.3).
var seed = int.Parse(Option("--seed") ?? "42");
var holders = int.Parse(Option("--holders") ?? "1000");
var rulesPath = Option("--rules") ?? Path.Combine("config", "rules.v1.json");
var outFolder = Option("--out") ?? "eval-reports";

// Printed rather than written to eval-reports/: a dry run is for trying a journey or a rule change live, not for comparing runs.
if (args.FirstOrDefault() == "scenario")
{
    var scenario = Scenario.Load(args.Length > 1 ? args[1] : throw new ArgumentException("scenario needs a file path."));
    var rules = RuleSet.Load(rulesPath);
    Console.Write(ScenarioReplay.Render(scenario, rules, ScenarioReplay.Replay(scenario, rules)));
    return;
}

// Datasets are not committed, so the seeded one is regenerated when missing; the same seed gives the same file.
var datasetPath = Path.Combine("datasets", $"seed-{seed}-holders-{holders}.jsonl");
if (!File.Exists(datasetPath))
    Console.WriteLine($"Generated {GenerateCommand.Run([GenerateCommand.Name, "--seed", $"{seed}", "--holders", $"{holders}"])}");

var ruleSet = RuleSet.Load(rulesPath);
EvaluationResult result;
using (var file = File.OpenRead(datasetPath))
    result = Evaluator.Evaluate(Dataset.Read(file), ruleSet);

var run = new EvaluationRun($"{DateTime.UtcNow:yyyyMMdd'T'HHmmss'Z'}-{ruleSet.Version}-seed{seed}", seed, holders, ruleSet.Version, datasetPath);
Directory.CreateDirectory(outFolder);
var reportPath = Path.Combine(outFolder, $"{run.RunId}.md");
File.WriteAllText(reportPath, Report.Render(result, run));

Console.WriteLine($"Wrote {reportPath}");
Console.WriteLine($"Detection rate {result.DetectionRate.Count}/{result.DetectionRate.Of}, " +
    $"false-positive rate {result.FalsePositiveRate.Count}/{result.FalsePositiveRate.Of}, " +
    $"early-detection rate {result.EarlyDetectionRate.Count}/{result.EarlyDetectionRate.Of}");

string? Option(string name)
{
    var i = Array.IndexOf(args, name);
    if (i < 0) return null;
    if (i + 1 >= args.Length) throw new ArgumentException($"{name} needs a value.");
    return args[i + 1];
}
