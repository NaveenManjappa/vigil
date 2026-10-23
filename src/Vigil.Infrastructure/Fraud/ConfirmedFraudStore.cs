using Vigil.Domain.Fraud;

namespace Vigil.Infrastructure.Fraud;

public sealed class ConfirmedFraudStore(VigilDbContext db)
{
    /// <summary>
    /// Idempotent: an Account already Confirmed Fraud keeps its first decision, which is returned either way.
    /// Callers hold the Account's lock, so no concurrent request can insert between the check and the insert.
    /// </summary>
    public async Task<ConfirmedFraud> RecordAsync(ConfirmedFraud fraud, CancellationToken ct = default)
    {
        if (await db.ConfirmedFrauds.FindAsync([fraud.AccountId], ct) is { } existing) return existing.ToDomain();

        db.ConfirmedFrauds.Add(new StoredConfirmedFraud { AccountId = fraud.AccountId, DecidedAt = fraud.DecidedAt, Source = fraud.Source });
        await db.SaveChangesAsync(ct);
        return fraud;
    }
}
