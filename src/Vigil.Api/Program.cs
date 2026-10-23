using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Neo4j.Driver;
using Scalar.AspNetCore;
using Vigil.Api.Alerts;
using Vigil.Api.Cases;
using Vigil.Api.Events;
using Vigil.Api.Fraud;
using Vigil.Api.Graph;
using Vigil.Domain.Detection;
using Vigil.Graph;
using Vigil.Infrastructure;
using Vigil.Infrastructure.Alerts;
using Vigil.Infrastructure.Audit;
using Vigil.Infrastructure.Cases;
using Vigil.Infrastructure.Events;
using Vigil.Infrastructure.Fraud;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddNpgsqlDbContext<VigilDbContext>("vigil");
builder.Services.AddLlm(builder.Configuration);
builder.Services.AddInvestigations();

// Loaded once at startup so a malformed rules file stops the API rather than failing mid-ingest (ADR-0002).
var rulesPath = builder.Configuration["Rules:Path"] ?? throw new InvalidOperationException("Rules:Path is not configured.");
var ruleSet = RuleSet.Load(Path.Combine(AppContext.BaseDirectory, rulesPath));
GraphRules.EnsureKnown(ruleSet);
builder.Services.AddSingleton(ruleSet);

// The AppHost supplies both; the driver is thread-safe and pools connections, so one serves the whole app.
var neo4jUri = builder.Configuration["Neo4j:Uri"] ?? throw new InvalidOperationException("Neo4j:Uri is not configured.");
var neo4jPassword = builder.Configuration["Neo4j:Password"] ?? throw new InvalidOperationException("Neo4j:Password is not configured.");
builder.Services.AddSingleton(_ => GraphDatabase.Driver(neo4jUri, AuthTokens.Basic("neo4j", neo4jPassword)));
builder.Services.AddSingleton<GraphProjector>();
builder.Services.AddSingleton<GraphRules>();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<EventStore>();
builder.Services.AddScoped<AlertStore>();
builder.Services.AddScoped<CaseStore>();
builder.Services.AddScoped<ConfirmedFraudStore>();
builder.Services.AddScoped<AuditStore>();
builder.Services.AddScoped<Ingestion>();
builder.Services.AddScoped<GraphRebuild>();

builder.Services.Configure<InvestigationOptions>(builder.Configuration.GetSection("Investigation"));
builder.Services.Configure<InterventionOptions>(builder.Configuration.GetSection("Intervention"));
builder.Services.AddSingleton<ICaseQueue, ChannelCaseQueue>();
builder.Services.AddHostedService<InvestigationWorker>();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();

if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<VigilDbContext>().Database.MigrateAsync();
}

await app.Services.GetRequiredService<GraphProjector>().EnsureConstraintsAsync(CancellationToken.None);

// `rebuild-graph` reuses the API's configuration and services, then exits instead of serving (ADR-0015).
if (args is ["rebuild-graph"])
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<GraphRebuild>().RunAsync(CancellationToken.None);
    return;
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapDefaultEndpoints();
app.MapEventEndpoints();
app.MapAlertEndpoints();
app.MapCaseEndpoints();
app.MapConfirmedFraudEndpoints();

app.Run();

// Exposed for WebApplicationFactory in tests.
public partial class Program;
