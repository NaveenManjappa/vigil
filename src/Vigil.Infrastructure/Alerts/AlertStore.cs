using Microsoft.EntityFrameworkCore;
using Vigil.Domain.Detection;

namespace Vigil.Infrastructure.Alerts;

public sealed class AlertStore(VigilDbContext db, TimeProvider clock)
{
    /// <summary>
    /// Merges freshly detected Alerts into what is stored: hits for rules not yet stored are appended,
    /// stored hits are never rewritten. Detection re-runs over the whole timeline on every ingest, so
    /// without this merge a re-sent or late event could silently change past evidence.
    /// </summary>
    public async Task SaveAsync(string accountId, IReadOnlyList<Alert> detected, CancellationToken ct = default)
    {
        // Detection yields at most one Alert per Account for now.
        if (detected.SingleOrDefault() is not { } alert) return;

        var now = clock.GetUtcNow();
        var stored = await db.Alerts.Include(a => a.Hits).SingleOrDefaultAsync(a => a.AccountId == accountId, ct);
        if (stored is null)
        {
            stored = new StoredAlert { AccountId = accountId, RulesVersion = alert.RulesVersion, CreatedAt = now };
            db.Alerts.Add(stored);
        }

        foreach (var hit in alert.Hits.Where(h => stored.Hits.All(s => s.RuleId != h.RuleId)))
            stored.Hits.Add(StoredRuleHit.From(hit));

        stored.Score = alert.Score;
        stored.Band = alert.Band;
        stored.RulesVersion = alert.RulesVersion;
        stored.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Riskiest first.</summary>
    public async Task<IReadOnlyList<StoredAlert>> ListAsync(RiskBand? band, CancellationToken ct = default) =>
        await db.Alerts
            .AsNoTracking()
            .Where(a => band == null || a.Band == band)
            .OrderByDescending(a => a.Score).ThenBy(a => a.CreatedAt)
            .ToListAsync(ct);

    public async Task<StoredAlert?> FindAsync(Guid alertId, CancellationToken ct = default) =>
        await db.Alerts
            .AsNoTracking()
            .Include(a => a.Hits.OrderBy(h => h.TriggeredAt))
            .SingleOrDefaultAsync(a => a.AlertId == alertId, ct);
}
