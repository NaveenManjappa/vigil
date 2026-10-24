using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Vigil.Agents;
using Vigil.Domain.Datasets;
using Vigil.Domain.Detection;
using Vigil.Evaluation;
using Vigil.Graph;
using Vigil.Simulator;
using Vigil.Simulator.Scenarios;

// [--seed 42] [--holders 1000] [--rules-off config/rules.v1.json] [--rules-on config/rules.v2.json] [--out eval-reports]
//   [--llm Fake|AzureOpenAI] [--agent-sample 20]: also dry-runs datasets/generated/ for Blind Spots
// scenario <file> [--rules config/rules.v2.json]
// The default run needs no API and no Postgres. With the default Fake LLM it is free to run any time (PROJECT.md
// Section 10.3); only --llm AzureOpenAI spends, and only on the Agent Sample (ADR-0016). Graph on needs Docker.

// tune --connection <postgres> [--rules] [--seed] [--holders] [--out]: the Rule-Tuning Report (ADR-0012).
if (args.FirstOrDefault() == Vigil.Evaluation.Tuning.TuneCommand.Name)
{
    Console.WriteLine($"Wrote {await Vigil.Evaluation.Tuning.TuneCommand.RunAsync(args)}");
    return;
}

var seed = int.Parse(Option("--seed") ?? "42");
var holders = int.Parse(Option("--holders") ?? "1000");
var offPath = Option("--rules-off") ?? Path.Combine("config", "rules.v1.json");
var onPath = Option("--rules-on") ?? EvaluationDefaults.RulesPath;
var outFolder = Option("--out") ?? "eval-reports";
var agentSampleSize = int.Parse(Option("--agent-sample") ?? $"{AgentSample.AgentSampleSize}");
var generatedFolder = Path.Combine("datasets", "generated");
// --rules once chose the only Rules Version; ignoring it now would quietly report the defaults instead.
if (args.FirstOrDefault() is not ("scenario" or "generate-scenarios") && args.Contains("--rules"))
    throw new ArgumentException("The full evaluation takes --rules-off and --rules-on; --rules is for the scenario dry run.");

// generate-scenarios [--count 5] [--rules config/rules.v2.json] [--llm Fake|AzureOpenAI] [--out datasets/generated]
// One call to the Scenario Generator; what it keeps is written for review and committed, and every later evaluation
// run dry-runs it for Blind Spots.
if (args.FirstOrDefault() == "generate-scenarios")
{
    var rulesPath = Option("--rules") ?? EvaluationDefaults.RulesPath;
    var folder = Option("--out") ?? generatedFolder;
    var examples = Directory.GetFiles("scenarios", "*.json").Order(StringComparer.Ordinal).ToList();
    var existing = Scenario.LoadAll("scenarios").Concat(GeneratedScenarios.LoadAll(folder)).ToList();

    await using var generatorServices = Llm(Option("--llm") ?? nameof(LlmProvider.Fake));
    var prices = generatorServices.GetRequiredService<LlmOptions>();
    var generated = await new ScenarioGenerator(generatorServices.GetRequiredService<IChatClient>()).GenerateAsync(
        int.Parse(Option("--count") ?? "5"), File.ReadAllText(rulesPath), [.. examples.Select(File.ReadAllText)], existing,
        CancellationToken.None);

    Directory.CreateDirectory(folder);
    foreach (var kept in generated.Result.Kept)
    {
        var path = Path.Combine(folder, $"{kept.Scenario.Name}.json");
        File.WriteAllText(path, kept.Json + "\n");
        Console.WriteLine($"Wrote {path}");
    }
    foreach (var reason in generated.Result.Rejected)
        Console.WriteLine($"Rejected {reason}");
    Console.WriteLine($"{prices.Provider}: {generated.Result.Kept.Count} kept, {generated.Result.Rejected.Count} rejected, " +
        $"{generated.InputTokens + generated.OutputTokens} tokens, £{prices.CostOf(generated.InputTokens, generated.OutputTokens)}");
    return;
}

// Printed rather than written to eval-reports/: a dry run is for trying a journey or a rule change live, not for comparing runs.
if (args.FirstOrDefault() == "scenario")
{
    var scenario = Scenario.Load(args.Length > 1 ? args[1] : throw new ArgumentException("scenario needs a file path."));
    // Defaults to the API's rules, so the dry run shows the live Score less only the Graph Rules it says it skipped.
    var rules = RuleSet.Load(Option("--rules") ?? EvaluationDefaults.RulesPath);
    Console.Write(ScenarioReplay.Render(scenario, rules, ScenarioReplay.Replay(scenario, rules)));
    return;
}

// Registered before the slow graph replay, so a bad --llm or a missing Azure setting fails in the first second.
await using var services = Llm(Option("--llm") ?? nameof(LlmProvider.Fake));
var chat = services.GetRequiredService<IChatClient>();
var llm = services.GetRequiredService<LlmOptions>();

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
// Kept until the agents are done: their graph tool reads the Neighbourhood from the same replayed graph.
await using var neo4j = onRules.Rules.Any(r => r.Kind == RuleKind.Graph) ? await ThrowawayNeo4j.StartAsync(CancellationToken.None) : null;
if (neo4j is not null)
{
    Console.WriteLine($"Replaying {dataset.Sum(a => a.Events.Count)} events into a throwaway Neo4j...");
    graphHits = await GraphReplay.CollectAsync(neo4j.Driver, dataset, onRules, CancellationToken.None);
}
var onResult = Evaluator.Evaluate(dataset, onRules, graphHits);

// Sampled from the graph-on Rules Version, the one the API runs, so the agents see the Cases production would open.
var sample = AgentSample.Draw(Evaluator.Score(dataset, onRules, graphHits), seed, agentSampleSize);
Console.WriteLine($"Investigating an Agent Sample of {sample.Count} Accounts with the {llm.Provider} LLM...");
var graph = neo4j is null ? null : new GraphNeighbourhood(neo4j.Driver);
var agents = await AgentEvaluator.RunAsync(sample, dataset, graph is null ? null : graph.ForAccountAsync, chat, llm, CancellationToken.None);

var run = new EvaluationRun($"{DateTime.UtcNow:yyyyMMdd'T'HHmmss'Z'}-{offRules.Version}-{onRules.Version}-seed{seed}", seed, holders, datasetPath);
Directory.CreateDirectory(outFolder);
var reportPath = Path.Combine(outFolder, $"{run.RunId}.md");
// Against the graph-on Rules Version, the one the API runs, so a Blind Spot is one production has today.
var blindSpots = BlindSpots.Find(GeneratedScenarios.LoadAll(generatedFolder), onRules);
File.WriteAllText(reportPath, Report.Render(run, offResult, onResult, agents, blindSpots));

Console.WriteLine($"Wrote {reportPath}");
foreach (var result in new[] { offResult, onResult })
    Console.WriteLine($"{result.RulesVersion}: detection rate {result.DetectionRate.Count}/{result.DetectionRate.Of}, " +
        $"false-positive rate {result.FalsePositiveRate.Count}/{result.FalsePositiveRate.Of}, " +
        $"early-detection rate {result.EarlyDetectionRate.Count}/{result.EarlyDetectionRate.Of}");
Console.WriteLine(agents.Summary());
Console.WriteLine($"Blind Spots: {blindSpots.BlindSpots.Count} of {blindSpots.Fraud} fraud Generated Scenarios never reached High on {blindSpots.RulesVersion}");

// The API's LLM wiring (ADR-0009), configured from environment variables such as Llm__Endpoint, Llm__Deployment and
// Llm__ApiKey. The prices are read from the API's own appsettings.json, so the £ figures match its token log.
ServiceProvider Llm(string provider)
{
    var configuration = new ConfigurationBuilder()
        .AddJsonFile(Path.GetFullPath(Path.Combine("src", "Vigil.Api", "appsettings.json")), optional: false)
        .AddEnvironmentVariables()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Llm:Provider"] = provider })
        .Build();

    return new ServiceCollection()
        .AddLogging(logging => logging.AddSimpleConsole(console => console.SingleLine = true))
        .AddLlm(configuration)
        .BuildServiceProvider();
}

string? Option(string name) => GenerateCommand.Option(args, name);
