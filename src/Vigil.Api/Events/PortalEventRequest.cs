using Vigil.Domain.Events;

namespace Vigil.Api.Events;

/// <summary>
/// Wire shape for one event. Everything is nullable and Type is a string so that bad input
/// reaches our validation and gets a precise error, instead of failing inside JSON binding.
/// </summary>
public sealed record PortalEventRequest(
    Guid? EventId,
    string? AccountId,
    string? HolderId,
    string? Type,
    DateTimeOffset? OccurredAt,
    string? DeviceId,
    string? IpAddress,
    string? Country,
    string? Channel,
    Dictionary<string, string>? Attributes)
{
    public IEnumerable<(string Field, string Error)> Validate()
    {
        if (EventId is null || EventId == Guid.Empty) yield return (nameof(EventId), "is required");
        if (string.IsNullOrWhiteSpace(AccountId)) yield return (nameof(AccountId), "is required");
        if (string.IsNullOrWhiteSpace(HolderId)) yield return (nameof(HolderId), "is required");
        if (OccurredAt is null) yield return (nameof(OccurredAt), "is required");
        if (string.IsNullOrWhiteSpace(Channel)) yield return (nameof(Channel), "is required");
        if (Country is not null && (Country.Length != 2 || !Country.All(char.IsAsciiLetterUpper)))
            yield return (nameof(Country), "must be an ISO 3166-1 alpha-2 code, e.g. GB");
        if (ParseType() is null) yield return (nameof(Type), $"'{Type}' is not a known event type");
    }

    // Enum.TryParse also accepts numbers ("3") and undefined values, which we must not let through.
    private EventType? ParseType() =>
        !string.IsNullOrWhiteSpace(Type) && !char.IsDigit(Type[0]) && Enum.TryParse<EventType>(Type, out var t)
            ? t
            : null;

    /// <summary>Only call after <see cref="Validate"/> returned no errors.</summary>
    public PortalEvent ToDomain() => new(
        EventId!.Value,
        AccountId!,
        HolderId!,
        ParseType()!.Value,
        OccurredAt!.Value,
        DeviceId,
        IpAddress,
        Country,
        Channel!,
        Attributes ?? []);
}
