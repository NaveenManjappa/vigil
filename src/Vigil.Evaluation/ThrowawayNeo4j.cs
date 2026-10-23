using Neo4j.Driver;
using Testcontainers.Neo4j;

namespace Vigil.Evaluation;

/// <summary>
/// A Neo4j container that lives only for one evaluation run, so replaying a Dataset never wipes the demo graph
/// the AppHost keeps in its volume.
/// </summary>
public sealed class ThrowawayNeo4j : IAsyncDisposable
{
    private readonly Neo4jContainer container;

    private ThrowawayNeo4j(Neo4jContainer container, IDriver driver)
    {
        this.container = container;
        Driver = driver;
    }

    public IDriver Driver { get; }

    public static async Task<ThrowawayNeo4j> StartAsync(CancellationToken ct)
    {
        // The same image as the AppHost, so Cypher behaves as it does in the API.
        var container = new Neo4jBuilder("neo4j:5-community").Build();
        await container.StartAsync(ct);
        // Testcontainers starts Neo4j with authentication disabled.
        return new(container, GraphDatabase.Driver(container.GetConnectionString(), AuthTokens.None));
    }

    public async ValueTask DisposeAsync()
    {
        await Driver.DisposeAsync();
        await container.DisposeAsync();
    }
}
