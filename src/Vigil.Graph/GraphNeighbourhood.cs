using Neo4j.Driver;
using Vigil.Domain.Graph;

namespace Vigil.Graph;

/// <summary>
/// Reads one Account's Neighbourhood from the Projection for the Investigator's graph tool (ADR-0007). One hop, like
/// the Graph Rules (ADR-0014), and only Linking Identifiers, so an IP address or Address never puts a stranger in it.
/// </summary>
public sealed class GraphNeighbourhood(IDriver driver)
{
    private static readonly List<string> Linking = [.. IdentifierKind.Linking.Select(kind => kind.Relationship)];

    // OPTIONAL MATCH keeps an identifier no other Account uses: that it is unshared is evidence too.
    private const string Query = """
        MATCH (:Account {accountId: $accountId})-[mine]->(identifier)
        WHERE type(mine) IN $linking
        OPTIONAL MATCH (identifier)<-[theirs]-(other:Account)
        WHERE other.accountId <> $accountId
        WITH identifier, mine, theirs, other ORDER BY other.accountId
        WITH identifier, mine, collect(CASE WHEN other IS NULL THEN null ELSE {
                 accountId: other.accountId,
                 eventId: theirs.firstEventId,
                 confirmedFraud: EXISTS { (other)<-[:CONCERNS]-(:Case {outcome: 'ConfirmedFraud'}) }
             } END) AS others
        RETURN labels(identifier)[0] AS kind, identifier.value AS value, mine.firstEventId AS eventId, others
        ORDER BY kind, value
        """;

    public async Task<Neighbourhood> ForAccountAsync(string accountId, CancellationToken ct)
    {
        var result = await driver.ExecutableQuery(Query)
            .WithParameters(new { accountId, linking = Linking })
            .ExecuteAsync(ct);

        var identifiers = result.Result.Select(row => new LinkedIdentifier(
            row["kind"].As<string>(),
            row["value"].As<string>(),
            Guid.Parse(row["eventId"].As<string>()),
            [.. row["others"].As<List<IDictionary<string, object>>>().Select(other => new LinkedAccount(
                other["accountId"].As<string>(),
                other["confirmedFraud"].As<bool>(),
                Guid.Parse(other["eventId"].As<string>())))]));

        return new Neighbourhood(accountId, [.. identifiers]);
    }
}
