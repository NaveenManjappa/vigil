using Microsoft.EntityFrameworkCore;
using Vigil.Domain.Cases;
using Vigil.Domain.Detection;
using Vigil.Infrastructure.Alerts;

namespace Vigil.Infrastructure.Cases;

/// <summary>Reads the Case queue. Cases are opened by <see cref="AlertStore"/>, inside the same ingest transaction as their Alert.</summary>
public sealed class CaseStore(VigilDbContext db)
{
    /// <summary>The holding-value tie-break in PROJECT.md Section 7.3 is left out until there is an Account entity to hold it.</summary>
    public async Task<IReadOnlyList<StoredCase>> QueueAsync(CaseStatus? status, CancellationToken ct = default) =>
        await db.Cases
            .AsNoTracking()
            .Include(c => c.Alert)
            .Where(c => status == null || c.Status == status)
            // Band is stored as text, so it needs an explicit rank: sorting the names would put High before Critical.
            .OrderBy(c => c.Alert.Band == RiskBand.Critical ? 0 : c.Alert.Band == RiskBand.High ? 1 : 2)
            .ThenByDescending(c => c.Alert.Score)
            .ThenBy(c => c.OpenedAt)
            .ToListAsync(ct);
}
