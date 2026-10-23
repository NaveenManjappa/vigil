using Microsoft.EntityFrameworkCore;

namespace Vigil.Infrastructure.Audit;

/// <summary>Appends and reads Audit Entries; there is deliberately nothing to change or remove one.</summary>
public sealed class AuditStore(VigilDbContext db)
{
    /// <summary>
    /// Staged, not saved: the caller saves it with the change it audits, so an entry exists exactly when its step did.
    /// </summary>
    public void Append(StoredAuditEntry entry) => db.AuditEntries.Add(entry);

    public async Task<IReadOnlyList<StoredAuditEntry>> ForCaseAsync(Guid caseId, CancellationToken ct = default) =>
        await db.AuditEntries.AsNoTracking().Where(a => a.CaseId == caseId).OrderBy(a => a.Position).ToListAsync(ct);
}
