using Neo4j.Driver;
using Vigil.Domain.Graph;

namespace Vigil.Graph;

/// <summary>
/// Reads one Account's links from the graph, one hop out, for the account view and the Investigator's
/// GetLinkedAccounts tool. Only Linking Identifiers count, so an IP address never puts a stranger in it.
/// </summary>
public sealed class GraphNeighbourhood(IDriver driver)
{
    // OPTIONAL MATCH keeps an identifier no other Account uses: that it is unshared is evidence too.
    private const string Query = """
        MATCH (account:Account {accountId: $accountId})-[mine]->(identifier)
        WHERE type(mine) IN $linking
        OPTIONAL MATCH (identifier)<-[theirs]-(other:Account)
        WHERE other.accountId <> $accountId AND type(theirs) = type(mine)
        WITH identifier, other ORDER BY other.accountId
        WITH identifier, collect(CASE WHEN other IS NULL THEN null
                                      ELSE {accountId: other.accountId, confirmedFraud: coalesce(other.confirmedFraud, false)} END) AS others
        RETURN labels(identifier)[0] AS kind, identifier.value AS value, others
        ORDER BY kind, value
        """;

    public async Task<Neighbourhood> ForAccountAsync(string accountId, CancellationToken ct)
    {
        var identifiers = await driver.ExecutableQuery(Query)
            .WithParameters(new { accountId, linking = IdentifierKind.LinkingRelationships })
            .ExecuteAsync(ct);

        var flag = await driver.ExecutableQuery("MATCH (a:Account {accountId: $accountId}) RETURN coalesce(a.confirmedFraud, false) AS fraud")
            .WithParameters(new { accountId })
            .ExecuteAsync(ct);

        return new Neighbourhood(
            accountId,
            flag.Result.Count > 0 && flag.Result[0]["fraud"].As<bool>(),
            [.. identifiers.Result.Select(row => new LinkedIdentifier(
                row["kind"].As<string>(),
                row["value"].As<string>(),
                [.. row["others"].As<List<IDictionary<string, object>>>().Select(other => new LinkedAccount(
                    other["accountId"].As<string>(), other["confirmedFraud"].As<bool>()))]))]);
    }
}
