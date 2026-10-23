using Vigil.Domain.Events;

namespace Vigil.Graph;

/// <summary>
/// One kind of identifier the Projection links an Account to: the node label, the relationship type and where
/// the value comes from on a Portal Event. Each kind is projected by one MERGE. Not every kind is a Linking
/// Identifier: IP addresses and Addresses are projected for exploration only.
/// </summary>
internal sealed record IdentifierKind(string Label, string Relationship, Func<PortalEvent, string?> ValueOf)
{
    public static readonly IReadOnlyList<IdentifierKind> All =
    [
        new("Device", "USED_DEVICE", e => e.DeviceId),
        new("IpAddress", "SEEN_FROM", e => e.IpAddress),
        new("Email", "HAS_EMAIL", e => e.Attributes.GetValueOrDefault("NewEmail")),
        new("Phone", "HAS_PHONE", e => e.Attributes.GetValueOrDefault("NewPhone")),
        new("BankAccount", "PAYS_TO", e => e.Attributes.GetValueOrDefault("NewBankAccountHash")),
        new("Address", "LIVES_AT", e => e.Attributes.GetValueOrDefault("NewPostcode")),
    ];

    // Labels and relationship types cannot be Cypher parameters, so they are written in; they only ever come from the list above.
    public string Cypher => $$"""
        UNWIND $rows AS row
        MERGE (a:Account {accountId: row.accountId})
        MERGE (i:{{Label}} {value: row.value})
        MERGE (a)-[r:{{Relationship}}]->(i)
        """ + KeepEarliestFirstSeen;

    // Events can arrive late (detection orders by OccurredAt, not arrival), so a later projection may carry an earlier time.
    internal const string KeepEarliestFirstSeen = """

          ON CREATE SET r.firstSeen = row.at
          ON MATCH SET r.firstSeen = CASE WHEN row.at < r.firstSeen THEN row.at ELSE r.firstSeen END
        """;
}
