using Vigil.Domain.Fraud;

namespace Vigil.Infrastructure.Fraud;

/// <summary>Keyed by AccountId, so the database itself refuses a second Confirmed Fraud for one Account.</summary>
public sealed class StoredConfirmedFraud
{
    public string AccountId { get; init; } = "";
    public DateTimeOffset DecidedAt { get; init; }
    public ConfirmedFraudSource Source { get; init; }

    public ConfirmedFraud ToDomain() => new(AccountId, DecidedAt, Source);
}
