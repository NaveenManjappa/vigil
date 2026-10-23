using Microsoft.EntityFrameworkCore;
using Vigil.Domain.Cases;
using Vigil.Domain.Detection;
using Vigil.Infrastructure.Cases;

namespace Vigil.Infrastructure.Alerts;

public sealed class AlertStore(VigilDbContext db, TimeProvider clock)
{
    /// <summary>
    /// Hands the Account's stored Alerts to <paramref name="detect"/> and persists what it adds. Detection
    /// only ever extends stored Alerts (ADR-0013), so this appends hits and Alerts and never rewrites a stored hit.
    /// An Alert that reaches High gets a Case the first time only; reaching Critical later just raises its Case's priority.
    /// Returns the Cases this call opened, which are the only ones that need an Investigation.
    /// </summary>
    public async Task<IReadOnlyList<Guid>> SaveAsync(
        string accountId, Func<IReadOnlyList<Alert>, IReadOnlyList<Alert>> detect, CancellationToken ct = default)
    {
        var stored = await db.Alerts
            .Include(a => a.Hits.OrderBy(h => h.TriggeredAt).ThenBy(h => h.Id))
            .Include(a => a.Case)
            .Where(a => a.AccountId == accountId)
            .OrderBy(a => a.CreatedAt).ThenBy(a => a.AlertId)
            .ToListAsync(ct);
        var detected = detect(stored.Select(a => a.ToDomain()).ToList());

        var now = clock.GetUtcNow();
        var opened = new List<StoredCase>();
        for (var i = 0; i < detected.Count; i++)
        {
            var alert = detected[i];
            var target = i < stored.Count ? stored[i] : null;
            if (target is null)
            {
                target = new StoredAlert { AccountId = accountId, RulesVersion = alert.RulesVersion, CreatedAt = now };
                db.Alerts.Add(target);
            }

            // Detection keeps stored hits first and in order, so everything past them is new.
            var added = alert.Hits.Skip(target.Hits.Count).ToList();
            if (added.Count == 0) continue;

            target.Hits.AddRange(added.Select(StoredRuleHit.From));
            target.Score = alert.Score;
            target.Band = alert.Band;
            target.UpdatedAt = now;

            if (target.Band >= RiskBand.High && target.Case is null)
            {
                target.Case = new StoredCase { Status = CaseStatus.Open, OpenedAt = now };
                opened.Add(target.Case);
            }
        }

        await db.SaveChangesAsync(ct);
        return opened.Select(c => c.CaseId).ToList();
    }

    public async Task<IReadOnlyList<StoredAlert>> ListAsync(RiskBand? band, CancellationToken ct = default) =>
        await db.Alerts
            .AsNoTracking()
            .Where(a => band == null || a.Band == band)
            .OrderByDescending(a => a.Score).ThenBy(a => a.CreatedAt)
            .ToListAsync(ct);

    public async Task<StoredAlert?> FindAsync(Guid alertId, CancellationToken ct = default) =>
        await db.Alerts
            .AsNoTracking()
            .Include(a => a.Hits.OrderBy(h => h.TriggeredAt).ThenBy(h => h.Id))
            .SingleOrDefaultAsync(a => a.AlertId == alertId, ct);
}
