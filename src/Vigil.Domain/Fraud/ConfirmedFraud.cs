namespace Vigil.Domain.Fraud;

/// <summary>Who decided an Account was fraud. Stage 2 only seeds known fraud; Stage 3 adds analyst decisions.</summary>
public enum ConfirmedFraudSource
{
    Seed,
}

/// <summary>
/// An Account known to be fraud, decided at <paramref name="DecidedAt"/>. Each Account is Confirmed Fraud at most once,
/// so recording it again changes nothing. G2 scores the Accounts that share a Linking Identifier with it.
/// </summary>
public sealed record ConfirmedFraud(string AccountId, DateTimeOffset DecidedAt, ConfirmedFraudSource Source);
