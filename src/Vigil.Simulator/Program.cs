using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vigil.Simulator;
using Vigil.Simulator.Scenarios;

if (args.FirstOrDefault() == GenerateCommand.Name)
{
    Console.WriteLine($"Wrote {GenerateCommand.Run(args)}");
    return;
}

var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();

// "https+http://api" is resolved by Aspire service discovery from the AppHost's WithReference(api).
builder.Services.AddHttpClient("api", c => c.BaseAddress = new Uri("https+http://api"));

using var host = builder.Build();
var http = host.Services.GetRequiredService<IHttpClientFactory>().CreateClient("api");
var log = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Simulator");

// [--scenario scenarios/my-story.json]: one file, resolved from the current directory; otherwise every shipped scenario.
var file = builder.Configuration["scenario"];
var scenarios = file is null
    ? Scenario.LoadAll(Path.Combine(AppContext.BaseDirectory, "scenarios"))
    : [Scenario.Load(file)];

foreach (var scenario in scenarios)
{
    log.LogInformation("Posted {Scenario}: {Result}", scenario.Name, await ScenarioPoster.PostAsync(http, scenario));
    if (scenario.ConfirmedFraud is { } fraud)
        log.LogInformation("Seeded {Account} as Confirmed Fraud, decided {DecidedAt}", fraud.AccountId, fraud.DecidedAt);
}
