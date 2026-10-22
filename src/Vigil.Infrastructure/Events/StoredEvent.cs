using Vigil.Domain.Events;

namespace Vigil.Infrastructure.Events;

/// <summary>
/// Persistence shape of a <see cref="PortalEvent"/>. Adds <see cref="ReceivedAt"/> (our clock, not the source's)
/// so late or out-of-order arrival can be measured without trusting it for detection.
/// </summary>
public sealed class StoredEvent
{
    public Guid EventId { get; init; }
    public required string AccountId { get; init; }
    public required string HolderId { get; init; }
    public EventType Type { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
    public DateTimeOffset ReceivedAt { get; init; }
    public string? DeviceId { get; init; }
    public string? IpAddress { get; init; }
    public string? Country { get; init; }
    public required string Channel { get; init; }
    public Dictionary<string, string> Attributes { get; init; } = [];

    public static StoredEvent From(PortalEvent e, DateTimeOffset receivedAt) => new()
    {
        EventId = e.EventId,
        AccountId = e.AccountId,
        HolderId = e.HolderId,
        Type = e.Type,
        OccurredAt = e.OccurredAt,
        ReceivedAt = receivedAt,
        DeviceId = e.DeviceId,
        IpAddress = e.IpAddress,
        Country = e.Country,
        Channel = e.Channel,
        Attributes = new Dictionary<string, string>(e.Attributes),
    };

    public PortalEvent ToDomain() =>
        new(EventId, AccountId, HolderId, Type, OccurredAt, DeviceId, IpAddress, Country, Channel, Attributes);
}
