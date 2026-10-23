namespace Vigil.Domain.Fraud;

/// <summary>Who decided an Account was fraud: seeded as known fraud, or an Analyst Decision with outcome ConfirmedFraud.</summary>
public enum ConfirmedFraudSource
{
    Seed,
    Analyst,
}

/// <summary>
/// An Account known to be fraud, decided at <paramref name="DecidedAt"/>. Each Account is Confirmed Fraud at most once,
/// so recording it again changes nothing. G2 scores the Accounts that share a Linking Identifier with it.
/// </summary>
public sealed record ConfirmedFraud(string AccountId, DateTimeOffset DecidedAt, ConfirmedFraudSource Source);
