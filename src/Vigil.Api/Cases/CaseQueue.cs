using System.Diagnostics;
using System.Threading.Channels;

namespace Vigil.Api.Cases;

/// <summary>A Case waiting for its Investigation, with the trace it was opened in so the Investigation can join it.</summary>
public sealed record QueuedCase(Guid CaseId, ActivityContext OpenedIn);

/// <summary>
/// Cases waiting for the Investigation worker. Behind an interface so the in-process channel can become a durable
/// broker without touching ingestion or the worker (ADR-0004).
/// </summary>
public interface ICaseQueue
{
    ValueTask EnqueueAsync(QueuedCase item, CancellationToken ct);
    IAsyncEnumerable<QueuedCase> DequeueAllAsync(CancellationToken ct);
}

/// <summary>
/// Bounded so a burst of new Cases slows ingestion down rather than growing memory without limit. Nothing is lost
/// on a crash: the Cases themselves are in Postgres and the worker re-queues unfinished ones on startup.
/// </summary>
public sealed class ChannelCaseQueue : ICaseQueue
{
    private readonly Channel<QueuedCase> _channel =
        Channel.CreateBounded<QueuedCase>(new BoundedChannelOptions(1000) { SingleReader = true });

    public ValueTask EnqueueAsync(QueuedCase item, CancellationToken ct) => _channel.Writer.WriteAsync(item, ct);

    public IAsyncEnumerable<QueuedCase> DequeueAllAsync(CancellationToken ct) => _channel.Reader.ReadAllAsync(ct);
}
