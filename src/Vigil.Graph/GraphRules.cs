using Neo4j.Driver;
using Vigil.Domain.Detection;
using Vigil.Domain.Events;

namespace Vigil.Graph;

/// <summary>
/// The rules that need other Accounts, so only the graph can check them. Each is one short Cypher query about the
/// identifiers on the new event. Run it after the event is projected, so the event's own links are in the graph.
/// </summary>
public sealed class GraphRules(IDriver driver)
{
    public async Task<IReadOnlyList<RuleHit>> HitsForAsync(PortalEvent e, RuleSet ruleSet, CancellationToken ct)
    {
        var hits = new List<RuleHit>();
        foreach (var rule in ruleSet.Rules.Where(r => r.Kind == RuleKind.Graph))
        {
            var hit = rule.Id switch
            {
                "G1" => await SharedBankAccountAsync(e, rule, ct),
                "G2" => await KnownFraudProximityAsync(e, rule, ct),
                _ => throw new InvalidDataException($"Rule {rule.Id}: no graph query is defined for this graph rule."),
            };
            if (hit is not null) hits.Add(hit);
        }

        return hits;
    }

    // G1: the new bank account is also paid out to by an Account of a different Holder.
    private async Task<RuleHit?> SharedBankAccountAsync(PortalEvent e, Rule rule, CancellationToken ct)
    {
        var bankAccount = e.Attributes.GetValueOrDefault("NewBankAccountHash");
        if (string.IsNullOrWhiteSpace(bankAccount)) return null;

        var result = await driver.ExecutableQuery("""
                MATCH (bank:BankAccount {value: $bankAccount})<-[:PAYS_TO]-(other:Account)<-[:OWNS]-(otherHolder:Holder)
                WHERE other.accountId <> $accountId AND otherHolder.holderId <> $holderId
                RETURN other.accountId AS otherAccount
                ORDER BY otherAccount
                LIMIT 1
                """)
            .WithParameters(new { bankAccount, accountId = e.AccountId, holderId = e.HolderId })
            .ExecuteAsync(ct);
        if (result.Result.Count == 0) return null;

        var explanation = rule.Explanation
            .Replace("{bankAccount}", bankAccount)
            .Replace("{account}", result.Result[0]["otherAccount"].As<string>());
        return new RuleHit(rule.Id, rule.Points, [e.EventId], e.OccurredAt, explanation);
    }

    // G2: a device, email, phone or bank account on the new event is also used by a Confirmed Fraud Account.
    private async Task<RuleHit?> KnownFraudProximityAsync(PortalEvent e, Rule rule, CancellationToken ct)
    {
        foreach (var kind in IdentifierKind.Linking)
        {
            var value = kind.ValueOf(e);
            if (string.IsNullOrWhiteSpace(value)) continue;

            var result = await driver.ExecutableQuery($$"""
                    MATCH (:{{kind.Label}} {value: $value})<-[:{{kind.Relationship}}]-(fraud:Account {confirmedFraud: true})
                    WHERE fraud.accountId <> $accountId
                    RETURN fraud.accountId AS fraudAccount
                    ORDER BY fraudAccount
                    LIMIT 1
                    """)
                .WithParameters(new { value, accountId = e.AccountId })
                .ExecuteAsync(ct);
            if (result.Result.Count == 0) continue;

            var explanation = rule.Explanation
                .Replace("{identifier}", kind.Label)
                .Replace("{value}", value)
                .Replace("{account}", result.Result[0]["fraudAccount"].As<string>());
            return new RuleHit(rule.Id, rule.Points, [e.EventId], e.OccurredAt, explanation);
        }

        return null;
    }
}
