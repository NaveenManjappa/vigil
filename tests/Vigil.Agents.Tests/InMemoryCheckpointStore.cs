using System.Text.Json;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Checkpointing;

namespace Vigil.Agents.Tests;

/// <summary>Checkpoints kept in memory, so the workflow's pause and resume are tested without Postgres.</summary>
public sealed class InMemoryCheckpointStore : JsonCheckpointStore
{
    private readonly List<(CheckpointInfo Info, string? Parent, JsonElement Value)> _rows = [];

    public override ValueTask<CheckpointInfo> CreateCheckpointAsync(string sessionId, JsonElement value, CheckpointInfo? parent = null)
    {
        var info = new CheckpointInfo(sessionId, Guid.NewGuid().ToString("N"));
        _rows.Add((info, parent?.CheckpointId, value.Clone()));
        return ValueTask.FromResult(info);
    }

    public override ValueTask<JsonElement> RetrieveCheckpointAsync(string sessionId, CheckpointInfo key) =>
        ValueTask.FromResult(_rows.Single(r => r.Info.Equals(key)).Value);

    public override ValueTask<IEnumerable<CheckpointInfo>> RetrieveIndexAsync(string sessionId, CheckpointInfo? withParent = null) =>
        ValueTask.FromResult(_rows
            .Where(r => r.Info.SessionId == sessionId && (withParent is null || r.Parent == withParent.CheckpointId))
            .Select(r => r.Info));
}

/// <summary>The Act hook, recording each decision it is handed.</summary>
public sealed class RecordingActStep : IActStep
{
    public List<(Guid CaseId, AnalystDecision Decision)> Acted { get; } = [];

    /// <summary>Makes the next call fail, as a database outage would.</summary>
    public bool FailNext { get; set; }

    public Task ActAsync(Guid caseId, AnalystDecision decision, CancellationToken ct)
    {
        if (FailNext)
        {
            FailNext = false;
            throw new InvalidOperationException("Act failed.");
        }
        Acted.Add((caseId, decision));
        return Task.CompletedTask;
    }
}
