using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Vigil.Domain.Events;

namespace Vigil.Infrastructure.Events;

public sealed record AppendResult(int Accepted, int AlreadyIngested);

/// <summary>
/// Append-only store of portal events. Appending is idempotent on EventId so sources can retry safely.
/// </summary>
public sealed class EventStore(VigilDbContext db, TimeProvider clock)
{
    public async Task<AppendResult> AppendAsync(IReadOnlyCollection<PortalEvent> events, CancellationToken ct = default)
    {
        try
        {
            return await TryAppendAsync(events, ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // A concurrent request inserted one of these IDs between our check and insert.
            // Re-running the check now classifies it correctly as already ingested.
            db.ChangeTracker.Clear();
            return await TryAppendAsync(events, ct);
        }
    }

    private async Task<AppendResult> TryAppendAsync(IReadOnlyCollection<PortalEvent> events, CancellationToken ct)
    {
        var distinct = events.DistinctBy(e => e.EventId).ToList();
        var ids = distinct.Select(e => e.EventId).ToList();

        var existing = await db.Events
            .Where(e => ids.Contains(e.EventId))
            .Select(e => e.EventId)
            .ToHashSetAsync(ct);

        var receivedAt = clock.GetUtcNow();
        var fresh = distinct.Where(e => !existing.Contains(e.EventId)).ToList();
        db.Events.AddRange(fresh.Select(e => StoredEvent.From(e, receivedAt)));
        await db.SaveChangesAsync(ct);

        return new AppendResult(fresh.Count, events.Count - fresh.Count);
    }

    /// <summary>Every stored event, in pages, for rebuilding the graph (ADR-0015).</summary>
    public async IAsyncEnumerable<PortalEvent> AllAsync(int pageSize, [EnumeratorCancellation] CancellationToken ct = default)
    {
        // Paged by EventId rather than one streamed query: Aspire's retrying execution strategy buffers a whole query's
        // results so it can replay them, which for the whole table would hold every event in memory.
        Guid? after = null;
        while (true)
        {
            var page = await db.Events
                .AsNoTracking()
                .Where(e => after == null || e.EventId.CompareTo(after.Value) > 0)
                .OrderBy(e => e.EventId)
                .Take(pageSize)
                .ToListAsync(ct);

            foreach (var stored in page) yield return stored.ToDomain();
            if (page.Count < pageSize) yield break;
            after = page[^1].EventId;
        }
    }

    /// <summary>In OccurredAt order, never arrival order, because detection scores an event where it happened (PROJECT.md Section 7.4).</summary>
    public async Task<IReadOnlyList<PortalEvent>> GetTimelineAsync(string accountId, CancellationToken ct = default)
    {
        var stored = await db.Events
            .AsNoTracking()
            .Where(e => e.AccountId == accountId)
            .OrderBy(e => e.OccurredAt).ThenBy(e => e.EventId)
            .ToListAsync(ct);

        return stored.Select(e => e.ToDomain()).ToList();
    }
}
