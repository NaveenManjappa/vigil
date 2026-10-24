using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vigil.Domain.Datasets;
using Vigil.Simulator;
using Vigil.Simulator.Scenarios;
using Vigil.Simulator.StandIn;

const string PostDataset = "post-dataset";
const string Decide = "decide";
const string DefaultDataset = "datasets/seed-42-holders-1000.jsonl";

var command = args.FirstOrDefault();
if (command == GenerateCommand.Name)
{
    Console.WriteLine($"Wrote {GenerateCommand.Run(args)}");
    return;
}

if (command == Decide && !args.Contains("--by-label"))
{
    Console.Error.WriteLine("decide needs --by-label: the Stand-in Analyst decides from each Account's Label.");
    Environment.ExitCode = 1;
    return;
}

// The Dataset commands keep their own options away from the host, whose command-line parser would read a bare
// --by-label as taking the next argument as its value.
var isDatasetCommand = command is PostDataset or Decide;
var builder = Host.CreateApplicationBuilder(isDatasetCommand ? [] : args);
builder.AddServiceDefaults();

// "https+http://api" is resolved by Aspire service discovery from the AppHost's WithReference(api).
builder.Services.AddHttpClient("api", c => c.BaseAddress = new Uri("https+http://api"));

using var host = builder.Build();
var http = host.Services.GetRequiredService<IHttpClientFactory>().CreateClient("api");
var log = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Simulator");

if (isDatasetCommand)
{
    var path = GenerateCommand.Option(args, "--dataset") ?? DefaultDataset;
    List<LabelledAccount> dataset;
    using (var file = File.OpenRead(path)) dataset = Dataset.Read(file).ToList();

    if (command == PostDataset)
    {
        log.LogInformation("Posted {Events} events from {Accounts} Accounts in {Dataset}",
            await DatasetPoster.PostAsync(http, dataset), dataset.Count, path);
        return;
    }

    var result = await DecideCommand.DecideAsync(http, dataset.ToDictionary(a => a.AccountId, a => a.Label));
    log.LogInformation("Stand-in Analyst decided {Decided} Cases; {Conflicts} were decided by someone else first", result.Decided, result.Conflicts);
    if (result.NotReady > 0)
        log.LogWarning("{NotReady} Cases are still Open or Investigating: wait for the worker and run decide again, or raise Investigation:DailyLimit", result.NotReady);
    if (result.Unlabelled.Count > 0)
        log.LogWarning("Left for a human, not in {Dataset}: {Accounts}", path, string.Join(", ", result.Unlabelled));
    return;
}

// [--scenario scenarios/my-story.json]: one file, resolved from the current directory; otherwise every shipped scenario.
var scenarioFile = builder.Configuration["scenario"];
var scenarios = scenarioFile is null
    ? Scenario.LoadAll(Path.Combine(AppContext.BaseDirectory, "scenarios"))
    : [Scenario.Load(scenarioFile)];

foreach (var scenario in scenarios)
{
    log.LogInformation("Posted {Scenario}: {Result}", scenario.Name, await ScenarioPoster.PostAsync(http, scenario));
    if (scenario.ConfirmedFraud is { } fraud)
        log.LogInformation("Seeded {Account} as Confirmed Fraud, decided {DecidedAt}", fraud.AccountId, fraud.DecidedAt);
}
