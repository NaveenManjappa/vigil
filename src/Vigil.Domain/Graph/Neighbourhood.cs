namespace Vigil.Domain.Graph;

/// <summary>
/// What the Projection knows about one Account's links, one hop out: each of its Linking Identifiers and the other
/// Accounts on it. It is the relational evidence the Investigator reasons over (ADR-0007).
/// </summary>
public sealed record Neighbourhood(string AccountId, IReadOnlyList<LinkedIdentifier> Identifiers);

/// <param name="Kind">The Projection's label: Device, Email, Phone or BankAccount.</param>
/// <param name="LinkEventId">The event that first linked this Account to the identifier.</param>
public sealed record LinkedIdentifier(string Kind, string Value, Guid LinkEventId, IReadOnlyList<LinkedAccount> OtherAccounts);

/// <param name="LinkEventId">The event that first linked that Account to the identifier.</param>
public sealed record LinkedAccount(string AccountId, bool IsConfirmedFraud, Guid LinkEventId);
