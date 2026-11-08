using Vigil.Domain.Events;

namespace Vigil.Graph;

/// <summary>
/// One kind of identifier the graph links an Account to: the node label, the relationship type and where the value
/// comes from on a Portal Event. Not every kind is a Linking Identifier: IP addresses and Addresses are shown in the
/// graph but never link Accounts, because households, offices and mobile carriers share them innocently.
/// </summary>
internal sealed record IdentifierKind(string Label, string Relationship, bool IsLinking, Func<PortalEvent, string?> ValueOf)
{
    public static readonly IReadOnlyList<IdentifierKind> All =
    [
        new("Device", "USED_DEVICE", IsLinking: true, e => e.DeviceId),
        new("IpAddress", "SEEN_FROM", IsLinking: false, e => e.IpAddress),
        new("Email", "HAS_EMAIL", IsLinking: true, e => e.Attributes.GetValueOrDefault("NewEmail")),
        new("Phone", "HAS_PHONE", IsLinking: true, e => e.Attributes.GetValueOrDefault("NewPhone")),
        new("BankAccount", "PAYS_TO", IsLinking: true, e => e.Attributes.GetValueOrDefault("NewBankAccountHash")),
        new("Address", "LIVES_AT", IsLinking: false, e => e.Attributes.GetValueOrDefault("NewPostcode")),
    ];

    public static IEnumerable<IdentifierKind> Linking => All.Where(kind => kind.IsLinking);

    /// <summary>The relationship types that link Accounts, for queries that must ignore IP addresses and Addresses.</summary>
    public static List<string> LinkingRelationships => [.. Linking.Select(kind => kind.Relationship)];

    // Labels and relationship types cannot be Cypher parameters, so they are written in; they only ever come from the list above.
    public string MergeCypher => $$"""
        MERGE (a:Account {accountId: $accountId})
        MERGE (i:{{Label}} {value: $value})
        MERGE (a)-[:{{Relationship}}]->(i)
        """;
}
