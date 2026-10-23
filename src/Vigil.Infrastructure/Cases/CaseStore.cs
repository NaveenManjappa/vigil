using Microsoft.EntityFrameworkCore;
using Vigil.Domain.Cases;
using Vigil.Domain.Detection;
using Vigil.Infrastructure.Alerts;

namespace Vigil.Infrastructure.Cases;

/// <summary>
/// Reads and moves Cases through their lifecycle. Cases are opened by <see cref="AlertStore"/>, inside the same
/// ingest transaction as their Alert.
/// </summary>
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

    /// <summary>Tracked, so the caller can change it and <see cref="SaveAsync"/>.</summary>
    public async Task<StoredCase?> FindAsync(Guid caseId, CancellationToken ct = default) =>
        await db.Cases.SingleOrDefaultAsync(c => c.CaseId == caseId, ct);

    public Task SaveAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);

    public async Task<int> InvestigationsStartedSinceAsync(DateTimeOffset since, CancellationToken ct = default) =>
        await db.Cases.CountAsync(c => c.InvestigationStartedAt >= since, ct);

    /// <summary>
    /// Cases whose Investigation never ran or was interrupted, oldest first. A Case the budget guard skipped is left
    /// out: it was skipped on purpose, so a restart must not spend budget on, say, a whole Dataset replay.
    /// </summary>
    public async Task<IReadOnlyList<Guid>> UnfinishedInvestigationsAsync(CancellationToken ct = default) =>
        await db.Cases
            .Where(c => (c.Status == CaseStatus.Open && !c.NotInvestigated) || c.Status == CaseStatus.Investigating)
            .OrderBy(c => c.OpenedAt)
            .Select(c => c.CaseId)
            .ToListAsync(ct);
}
