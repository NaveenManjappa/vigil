using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Neo4j.Driver;
using Scalar.AspNetCore;
using Vigil.Agents;
using Vigil.Api.Accounts;
using Vigil.Api.Cases;
using Vigil.Api.Data;
using Vigil.Api.Events;
using Vigil.Domain.Detection;
using Vigil.Graph;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddNpgsqlDbContext<VigilDbContext>("vigil");

// The rules are loaded once at startup, so a malformed rules file stops the API rather than failing mid-ingest (ADR-0002).
var rulesPath = builder.Configuration["Rules:Path"] ?? throw new InvalidOperationException("Rules:Path is not configured.");
builder.Services.AddSingleton(RuleSet.Load(Path.Combine(AppContext.BaseDirectory, rulesPath)));

// The AppHost supplies both. The driver pools connections, so one serves the whole app.
var neo4jUri = builder.Configuration["Neo4j:Uri"] ?? throw new InvalidOperationException("Neo4j:Uri is not configured.");
var neo4jPassword = builder.Configuration["Neo4j:Password"] ?? throw new InvalidOperationException("Neo4j:Password is not configured.");
builder.Services.AddSingleton(_ => GraphDatabase.Driver(neo4jUri, AuthTokens.Basic("neo4j", neo4jPassword)));
builder.Services.AddSingleton<GraphProjector>();
builder.Services.AddSingleton<GraphRules>();
builder.Services.AddSingleton<GraphNeighbourhood>();

// The chat model the Investigator talks to: Fake or Azure OpenAI, chosen by Llm:Provider (ADR-0009).
builder.Services.AddLlm(builder.Configuration);
builder.Services.AddScoped<AccountData>();
builder.Services.AddScoped<IAccountData>(sp => sp.GetRequiredService<AccountData>());
builder.Services.AddScoped<Investigator>();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<Ingestion>();
builder.Services.Configure<InterventionOptions>(builder.Configuration.GetSection("Intervention"));

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

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();

    // Development only: empties Postgres and Neo4j, so the walkthrough can start again from step 0.
    app.MapPost("/reset", async (VigilDbContext db, GraphProjector graph, CancellationToken ct) =>
        {
            await db.Cases.ExecuteDeleteAsync(ct);
            await db.Alerts.ExecuteDeleteAsync(ct);
            await db.Events.ExecuteDeleteAsync(ct);
            await graph.DeleteAllAsync(ct);
            return TypedResults.Ok(new { Message = "All events, Alerts, Cases and graph nodes are deleted." });
        })
        .WithTags("0. Setup")
        .WithSummary("Delete everything, to start the walkthrough again. Development only.");
}

app.MapGet("/rules", (RuleSet rules) => TypedResults.Ok(rules))
    .WithTags("0. Setup")
    .WithSummary("The Rules in force: their points, Windows and the band thresholds.");

app.MapDefaultEndpoints();
app.MapEventEndpoints();
app.MapAccountEndpoints();
app.MapCaseEndpoints();

app.Run();

// Exposed for WebApplicationFactory in tests.
public partial class Program;
