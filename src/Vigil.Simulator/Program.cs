using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
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
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

Scenario[] scenarios = [new MargaretAto(), new MrPatelHouseMove()];

foreach (var scenario in scenarios)
{
    var response = await http.PostAsJsonAsync("/events", scenario.Events(), json);
    var body = await response.Content.ReadAsStringAsync();
    response.EnsureSuccessStatusCode();
    log.LogInformation("Posted {Scenario}: {Result}", scenario.Name, body);
}
