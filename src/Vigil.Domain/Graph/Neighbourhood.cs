namespace Vigil.Domain.Graph;

/// <summary>
/// What the graph knows about one Account's links, one hop out: each Linking Identifier it uses and the other
/// Accounts that use it too.
/// </summary>
public sealed record Neighbourhood(string AccountId, bool IsConfirmedFraud, IReadOnlyList<LinkedIdentifier> Identifiers);

/// <param name="Kind">The graph label: Device, Email, Phone or BankAccount.</param>
public sealed record LinkedIdentifier(string Kind, string Value, IReadOnlyList<LinkedAccount> OtherAccounts);

public sealed record LinkedAccount(string AccountId, bool IsConfirmedFraud);
