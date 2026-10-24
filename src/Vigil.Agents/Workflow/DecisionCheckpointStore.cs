using System.Text.Json;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Checkpointing;

namespace Vigil.Agents;

/// <summary>
/// The checkpoints a decision run sees: it reads the Case's stored chain but keeps what it writes in memory. So the
/// pause at Await Approval stays the Case's latest checkpoint for good, and two decisions racing on one Case both
/// resume from it and both reach Act, where the one-decision-per-Case constraint turns the loser away. A run resumed
/// from a checkpoint past Act would have no request to answer and would wait forever.
/// </summary>
internal sealed class DecisionCheckpointStore(JsonCheckpointStore stored) : JsonCheckpointStore
{
    private readonly Dictionary<string, JsonElement> _written = [];

    public override ValueTask<CheckpointInfo> CreateCheckpointAsync(string sessionId, JsonElement value, CheckpointInfo? parent = null)
    {
        var info = new CheckpointInfo(sessionId, Guid.NewGuid().ToString("N"));
        _written[info.CheckpointId] = value.Clone();
        return ValueTask.FromResult(info);
    }

    public override ValueTask<JsonElement> RetrieveCheckpointAsync(string sessionId, CheckpointInfo key) =>
        _written.TryGetValue(key.CheckpointId, out var value)
            ? ValueTask.FromResult(value)
            : stored.RetrieveCheckpointAsync(sessionId, key);

    public override ValueTask<IEnumerable<CheckpointInfo>> RetrieveIndexAsync(string sessionId, CheckpointInfo? withParent = null) =>
        stored.RetrieveIndexAsync(sessionId, withParent);
}
