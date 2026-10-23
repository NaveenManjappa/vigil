namespace Vigil.Domain.Events;

/// <summary>
/// The canonical shape every source-system event is normalised into before detection sees it.
/// </summary>
/// <param name="EventId">Source-assigned identity; ingestion is idempotent on it.</param>
/// <param name="HolderId">Kept even though Holder:Account is 1:1 in the MVP, so 1:N needs no schema change.</param>
/// <param name="OccurredAt">When it happened at the source. Detection orders by this, never by arrival.</param>
/// <param name="Attributes">Type-specific details, e.g. NewEmail, NewBankAccountHash, Amount.</param>
public sealed record PortalEvent(
    Guid EventId,
    string AccountId,
    string HolderId,
    EventType Type,
    DateTimeOffset OccurredAt,
    string? DeviceId,
    string? IpAddress,
    string? Country,
    string Channel,
    IReadOnlyDictionary<string, string> Attributes);
