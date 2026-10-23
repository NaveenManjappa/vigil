using System.Text.Json;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Checkpointing;
using Microsoft.EntityFrameworkCore;

namespace Vigil.Infrastructure.Workflow;

/// <summary>One workflow checkpoint, written after a superstep. The session is the Case, so a Case's rows are its chain.</summary>
public sealed class StoredCheckpoint
{
    /// <summary>Insertion order, which is the order the workflow wrote them: the last row is the Case's latest checkpoint.</summary>
    public long Position { get; init; }

    public required string SessionId { get; init; }
    public required string CheckpointId { get; init; }
    public string? ParentId { get; init; }
    public required string Payload { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>
/// MAF's checkpoints in the <c>workflow_checkpoints</c> table, so a Case paused in AwaitingApproval survives an API
/// restart (ADR-0005). MAF ships no database store; this is its JSON store contract over EF Core.
/// </summary>
public sealed class PostgresCheckpointStore(VigilDbContext db, TimeProvider clock) : JsonCheckpointStore
{
    public override async ValueTask<CheckpointInfo> CreateCheckpointAsync(string sessionId, JsonElement value, CheckpointInfo? parent = null)
    {
        var info = new CheckpointInfo(sessionId, Guid.NewGuid().ToString("N"));
        db.Checkpoints.Add(new StoredCheckpoint
        {
            SessionId = sessionId,
            CheckpointId = info.CheckpointId,
            ParentId = parent?.CheckpointId,
            // Copied out now: the element may sit on a pooled buffer MAF reuses once this returns.
            Payload = value.GetRawText(),
            CreatedAt = clock.GetUtcNow(),
        });
        await db.SaveChangesAsync();
        return info;
    }

    public override async ValueTask<JsonElement> RetrieveCheckpointAsync(string sessionId, CheckpointInfo key)
    {
        var payload = await db.Checkpoints
            .Where(c => c.SessionId == sessionId && c.CheckpointId == key.CheckpointId)
            .Select(c => c.Payload)
            .SingleOrDefaultAsync()
            ?? throw new KeyNotFoundException($"No checkpoint {key.CheckpointId} for session {sessionId}.");

        using var document = JsonDocument.Parse(payload);
        return document.RootElement.Clone();
    }

    public override async ValueTask<IEnumerable<CheckpointInfo>> RetrieveIndexAsync(string sessionId, CheckpointInfo? withParent = null)
    {
        var parentId = withParent?.CheckpointId;
        var ids = await db.Checkpoints
            .Where(c => c.SessionId == sessionId && (parentId == null || c.ParentId == parentId))
            .OrderBy(c => c.Position)
            .Select(c => c.CheckpointId)
            .ToListAsync();
        return ids.Select(id => new CheckpointInfo(sessionId, id));
    }
}
