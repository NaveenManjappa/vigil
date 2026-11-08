using Vigil.Domain.Events;

namespace Vigil.Api.Events;

/// <summary>
/// The body of <c>POST /events</c>: one Portal Event. Everything is nullable and Type is a string, so bad input reaches
/// our validation and gets a precise error instead of failing inside JSON binding.
/// </summary>
/// <param name="EventId">Optional: Vigil makes one up when it is missing. Send the same one twice and the second is ignored.</param>
/// <param name="OccurredAt">When it happened at the source. Required, because the rules measure time between events.</param>
/// <param name="Channel">Optional: Web when missing.</param>
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
    public Dictionary<string, string[]> Validate()
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(AccountId)) errors["accountId"] = ["is required"];
        if (string.IsNullOrWhiteSpace(HolderId)) errors["holderId"] = ["is required"];
        if (OccurredAt is null) errors["occurredAt"] = ["is required"];
        if (ParseType() is null) errors["type"] = [$"'{Type}' is not one of: {string.Join(", ", Enum.GetNames<EventType>())}"];
        if (Country is not null && (Country.Length != 2 || !Country.All(char.IsAsciiLetterUpper)))
            errors["country"] = ["must be a two-letter ISO 3166-1 code, e.g. GB"];
        return errors;
    }

    // Enum.TryParse also accepts numbers ("3") and undefined values, which we must not let through.
    private EventType? ParseType() =>
        !string.IsNullOrWhiteSpace(Type) && !char.IsDigit(Type[0]) && Enum.TryParse<EventType>(Type, out var t) ? t : null;

    /// <summary>Only call after <see cref="Validate"/> returned no errors.</summary>
    public PortalEvent ToDomain() => new(
        EventId ?? Guid.NewGuid(),
        AccountId!,
        HolderId!,
        ParseType()!.Value,
        OccurredAt!.Value,
        DeviceId,
        IpAddress,
        Country,
        string.IsNullOrWhiteSpace(Channel) ? "Web" : Channel,
        Attributes ?? []);
}
