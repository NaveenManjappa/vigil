using Microsoft.EntityFrameworkCore;
using Vigil.Infrastructure;

namespace Vigil.Evaluation.Tuning;

/// <summary>Reads Analyst Decision → Case → Alert → Rule Hits from the API's Postgres. Read-only: tuning writes nothing.</summary>
public static class DecisionReader
{
    public static VigilDbContext Open(string connectionString) =>
        new(new DbContextOptionsBuilder<VigilDbContext>().UseNpgsql(connectionString).Options);

    public static async Task<IReadOnlyList<DecidedCase>> ReadAsync(string connectionString, CancellationToken ct = default)
    {
        await using var db = Open(connectionString);
        var rows = await db.Cases
            .AsNoTracking()
            .Where(c => c.Decision != null)
            .Select(c => new
            {
                c.CaseId,
                c.Decision!.Outcome,
                c.Decision.AnalystId,
                RuleIds = c.Alert.Hits.Select(h => h.RuleId).ToList(),
            })
            .ToListAsync(ct);

        return [.. rows.Select(r => new DecidedCase(r.CaseId, r.Outcome, r.AnalystId, r.RuleIds))];
    }
}
