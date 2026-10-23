using Neo4j.Driver;
using Testcontainers.Neo4j;

namespace Vigil.Graph.Tests;

/// <summary>A throwaway Neo4j shared by one test class, so Cypher runs against the real engine.</summary>
public sealed class Neo4jFixture : IAsyncLifetime
{
    private readonly Neo4jContainer _neo4j = new Neo4jBuilder("neo4j:5-community").Build();

    public IDriver Driver { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        await _neo4j.StartAsync();
        // Testcontainers starts Neo4j with authentication disabled.
        Driver = GraphDatabase.Driver(_neo4j.GetConnectionString(), AuthTokens.None);
    }

    public async ValueTask DisposeAsync()
    {
        await Driver.DisposeAsync();
        await _neo4j.DisposeAsync();
    }
}
