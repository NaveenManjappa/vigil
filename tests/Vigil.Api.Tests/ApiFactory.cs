using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.Neo4j;
using Testcontainers.PostgreSql;

namespace Vigil.Api.Tests;

/// <summary>
/// Runs the real API against throwaway Postgres and Neo4j containers, so tests exercise the same SQL,
/// jsonb mapping, primary-key behaviour and Cypher as production. One pair is shared per test class.
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string Neo4jPassword = "vigil-test-password";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    // Testcontainers turns Neo4j authentication off; it is switched back on so the API's password wiring is exercised.
    private readonly Neo4jContainer _neo4j = new Neo4jBuilder("neo4j:5-community")
        .WithEnvironment("NEO4J_AUTH", $"neo4j/{Neo4jPassword}")
        .Build();

    public async ValueTask InitializeAsync() => await Task.WhenAll(_postgres.StartAsync(), _neo4j.StartAsync());

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:vigil", _postgres.GetConnectionString());
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("Neo4j:Uri", _neo4j.GetConnectionString());
        builder.UseSetting("Neo4j:Password", Neo4jPassword);
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
        await _neo4j.DisposeAsync();
    }
}

/// <summary>For tests about opening and ordering Cases, which the Investigation worker would otherwise move on.</summary>
public sealed class InvestigationsOffApiFactory : ApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Investigation:Enabled", "false");
    }
}
