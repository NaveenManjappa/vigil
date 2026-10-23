using Neo4j.Driver;
using Vigil.Domain.Detection;
using Vigil.Domain.Events;

namespace Vigil.Graph;

/// <summary>
/// Runs a rule set's Graph Rules for one Account against the Projection and returns candidate Rule Hits for the
/// Detector, which decides which ones count (ADR-0014). Each Graph Rule is one short, one-hop Cypher query that
/// reads the graph as it is now; the rules file supplies its points and explanation. It also finds a batch's
/// Neighbours, the other Accounts whose Graph Rule Hits the batch can change.
/// </summary>
public sealed class GraphRules(IDriver driver)
{
    private static readonly HashSet<string> Known = ["G1", "G3"];

    /// <summary>Called at startup, so a graph rule in the rules file with no query behind it stops the API.</summary>
    public static void EnsureKnown(RuleSet ruleSet)
    {
        foreach (var rule in ruleSet.Rules.Where(r => r.Kind == RuleKind.Graph))
        {
            if (!Known.Contains(rule.Id))
                throw new InvalidDataException($"Rule {rule.Id}: no graph query is defined for this graph rule.");

            // A threshold of 1 would make every Account with any Linking Identifier a Ring.
            if (rule.Id == "G3" && rule.AccountThreshold < 2)
                throw new InvalidDataException($"Rule {rule.Id}: a Ring needs an accountThreshold of at least 2.");
        }
    }

    public async Task<IReadOnlyList<RuleHit>> HitsForAsync(string accountId, RuleSet ruleSet, CancellationToken ct)
    {
        var hits = new List<RuleHit>();
        foreach (var rule in ruleSet.Rules.Where(r => r.Kind == RuleKind.Graph))
        {
            hits.AddRange(rule.Id switch
            {
                "G1" => await SharedBankAccountAsync(accountId, rule, ct),
                "G3" => await RingAsync(accountId, rule, ct),
                _ => throw new InvalidDataException($"Rule {rule.Id}: no graph query is defined for this graph rule."),
            });
        }

        return hits;
    }

    /// <summary>
    /// The Accounts outside the batch that share a Linking Identifier with it: a new link can change their Graph Rule
    /// Hits, so ingestion re-checks them (ADR-0015). Sorted, so their locks are always taken in the same order.
    /// </summary>
    public async Task<IReadOnlyList<string>> NeighboursOfAsync(IReadOnlyCollection<PortalEvent> batch, CancellationToken ct)
    {
        var batchAccounts = batch.Select(e => e.AccountId).Distinct().ToList();
        var neighbours = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var kind in IdentifierKind.Linking)
        {
            var values = kind.ValuesIn(batch).ToList();
            if (values.Count == 0) continue;

            // The label and relationship type are written in, as in the projection; they only come from IdentifierKind.
            var result = await driver.ExecutableQuery($"""
                    MATCH (i:{kind.Label})<-[:{kind.Relationship}]-(neighbour:Account)
                    WHERE i.value IN $values AND NOT neighbour.accountId IN $batchAccounts
                    RETURN DISTINCT neighbour.accountId AS accountId
                    """)
                .WithParameters(new { values, batchAccounts })
                .ExecuteAsync(ct);
            neighbours.UnionWith(result.Result.Select(row => row["accountId"].As<string>()));
        }

        return [.. neighbours];
    }

    // G1: this Account pays out to a bank account that an Account of a different Holder also pays out to.
    private const string SharedBankAccount = """
        MATCH (holder:Holder)-[:OWNS]->(:Account {accountId: $accountId})-[mine:PAYS_TO]->(bank:BankAccount)
              <-[theirs:PAYS_TO]-(other:Account)<-[:OWNS]-(otherHolder:Holder)
        WHERE otherHolder <> holder
        RETURN bank.value AS bankAccount, other.accountId AS otherAccount,
               mine.firstSeen AS mineAt, mine.firstEventId AS mineEvent,
               theirs.firstSeen AS theirsAt, theirs.firstEventId AS theirsEvent
        ORDER BY otherAccount
        """;

    private async Task<List<RuleHit>> SharedBankAccountAsync(string accountId, Rule rule, CancellationToken ct)
    {
        var result = await driver.ExecutableQuery(SharedBankAccount).WithParameters(new { accountId }).ExecuteAsync(ct);

        return result.Result.Select(row =>
        {
            var mine = Link(row, "mineAt", "mineEvent");
            var theirs = Link(row, "theirsAt", "theirsEvent");

            // Whichever link formed second completed the pair, so it triggers the hit and its event goes last.
            // On a tie this Account's own event goes last, since it is the one being scored.
            var (first, completing) = theirs.At > mine.At ? (mine, theirs) : (theirs, mine);

            var explanation = rule.Explanation
                .Replace("{bankAccount}", row["bankAccount"].As<string>())
                .Replace("{account}", row["otherAccount"].As<string>());
            return new RuleHit(rule.Id, rule.Points, [first.EventId, completing.EventId], completing.At, explanation);
        }).ToList();
    }

    // G3: one of this Account's Linking Identifiers is shared by at least the threshold number of Accounts, this one
    // included. Only the identifier itself is checked, not a group joined through several hops (ADR-0014).
    private const string Ring = """
        MATCH (:Account {accountId: $accountId})-[mine]->(identifier)<-[theirs]-(other:Account)
        WHERE type(mine) IN $linking
        WITH identifier, mine, theirs ORDER BY theirs.firstSeen, other.accountId
        WITH identifier, mine, collect({at: theirs.firstSeen, eventId: theirs.firstEventId}) AS others
        WHERE size(others) + 1 >= $threshold
        RETURN labels(identifier)[0] AS kind, identifier.value AS value, size(others) + 1 AS accounts,
               mine.firstSeen AS mineAt, mine.firstEventId AS mineEvent, others[..($threshold - 1)] AS earliest
        ORDER BY kind, value
        """;

    private async Task<List<RuleHit>> RingAsync(string accountId, Rule rule, CancellationToken ct)
    {
        var linking = IdentifierKind.Linking.Select(kind => kind.Relationship).ToList();
        var result = await driver.ExecutableQuery(Ring)
            .WithParameters(new { accountId, linking, threshold = rule.AccountThreshold })
            .ExecuteAsync(ct);

        return result.Result.Select(row =>
        {
            // This Account and the earliest others make the smallest group that reaches the threshold; whichever of
            // those links formed last brought it there. OrderBy is stable, so on a tie this Account's own event goes last.
            var links = row["earliest"].As<List<IDictionary<string, object>>>()
                .Select(other => Link(other["at"], other["eventId"]))
                .Append(Link(row["mineAt"], row["mineEvent"]))
                .OrderBy(link => link.At)
                .ToList();

            var explanation = rule.Explanation
                .Replace("{identifier}", row["kind"].As<string>())
                .Replace("{value}", row["value"].As<string>())
                .Replace("{accounts}", row["accounts"].As<string>())
                .Replace("{threshold}", rule.AccountThreshold.ToString());
            return new RuleHit(rule.Id, rule.Points, [.. links.Select(l => l.EventId)], links[^1].At, explanation);
        }).ToList();
    }

    private static (DateTimeOffset At, Guid EventId) Link(IRecord row, string at, string eventId) =>
        Link(row[at], row[eventId]);

    private static (DateTimeOffset At, Guid EventId) Link(object at, object eventId) =>
        (at.As<ZonedDateTime>().ToDateTimeOffset(), Guid.Parse(eventId.As<string>()));
}
