using Vigil.Domain.Events;

namespace Vigil.Api.Data;

/// <summary>A <see cref="PortalEvent"/> as it is stored in the <c>events</c> table.</summary>
public sealed class StoredEvent
{
    public Guid EventId { get; init; }
    public required string AccountId { get; init; }
    public required string HolderId { get; init; }
    public EventType Type { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
    public string? DeviceId { get; init; }
    public string? IpAddress { get; init; }
    public string? Country { get; init; }
    public required string Channel { get; init; }
    public Dictionary<string, string> Attributes { get; init; } = [];

    public static StoredEvent From(PortalEvent e) => new()
    {
        EventId = e.EventId,
        AccountId = e.AccountId,
        HolderId = e.HolderId,
        Type = e.Type,
        OccurredAt = e.OccurredAt,
        DeviceId = e.DeviceId,
        IpAddress = e.IpAddress,
        Country = e.Country,
        Channel = e.Channel,
        Attributes = new Dictionary<string, string>(e.Attributes),
    };

    public PortalEvent ToDomain() =>
        new(EventId, AccountId, HolderId, Type, OccurredAt, DeviceId, IpAddress, Country, Channel, Attributes);
}
