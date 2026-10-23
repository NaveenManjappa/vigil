using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using Vigil.Api.Alerts;
using Vigil.Api.Cases;
using Vigil.Api.Events;
using Vigil.Domain.Detection;
using Vigil.Infrastructure;
using Vigil.Infrastructure.Alerts;
using Vigil.Infrastructure.Cases;
using Vigil.Infrastructure.Events;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddNpgsqlDbContext<VigilDbContext>("vigil");

// Loaded once at startup so a malformed rules file stops the API rather than failing mid-ingest (ADR-0002).
var rulesPath = builder.Configuration["Rules:Path"] ?? throw new InvalidOperationException("Rules:Path is not configured.");
builder.Services.AddSingleton(RuleSet.Load(Path.Combine(AppContext.BaseDirectory, rulesPath)));

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<EventStore>();
builder.Services.AddScoped<AlertStore>();
builder.Services.AddScoped<CaseStore>();
builder.Services.AddScoped<Ingestion>();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();

if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<VigilDbContext>().Database.MigrateAsync();
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

app.Run();

// Exposed for WebApplicationFactory in tests.
public partial class Program;
