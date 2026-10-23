using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Vigil.Agents.Tests;

/// <summary>
/// Plays a scripted reply per call and records everything it was sent, so tests can read every prompt the
/// model would have seen. No test calls a real LLM.
/// </summary>
public sealed class RecordingChatClient : IChatClient
{
    private readonly Queue<Func<ChatResponse>> _script = new();

    public List<(IReadOnlyList<ChatMessage> Messages, ChatOptions? Options)> Calls { get; } = [];

    public RecordingChatClient CallsTools(params FunctionCallContent[] calls)
    {
        _script.Enqueue(() => new ChatResponse(new ChatMessage(ChatRole.Assistant, [.. calls]))
        {
            Usage = new UsageDetails { InputTokenCount = 100, OutputTokenCount = 10 },
        });
        return this;
    }

    public RecordingChatClient Replies(string text)
    {
        _script.Enqueue(() => new ChatResponse(new ChatMessage(ChatRole.Assistant, text))
        {
            Usage = new UsageDetails { InputTokenCount = 200, OutputTokenCount = 20 },
        });
        return this;
    }

    /// <summary>The call fails the way a provider outage or timeout does.</summary>
    public RecordingChatClient Throws(Exception exception)
    {
        _script.Enqueue(() => throw exception);
        return this;
    }

    /// <summary>
    /// Every piece of text the model was given: instructions, messages, and tool calls and results. Instructions
    /// travel in <see cref="ChatOptions.Instructions"/>, not as a message, so they are read from there.
    /// </summary>
    public IEnumerable<string> EverythingSent() => Calls.SelectMany(call =>
        new[] { call.Options?.Instructions ?? "" }.Concat(call.Messages.SelectMany(m => m.Contents.Select(c => c switch
        {
            TextContent text => text.Text,
            FunctionCallContent fc => string.Join(" ", fc.Arguments?.Select(a => $"{a.Key}={a.Value}") ?? []),
            FunctionResultContent fr => fr.Result?.ToString() ?? "",
            _ => "",
        }))));

    /// <summary>The tool results sent back to the model, by tool call id.</summary>
    public Dictionary<string, string> ToolResults() => Calls
        .SelectMany(call => call.Messages)
        .SelectMany(m => m.Contents.OfType<FunctionResultContent>())
        .DistinctBy(r => r.CallId)
        .ToDictionary(r => r.CallId, r => r.Result?.ToString() ?? "");

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        // As a real provider would, so a cancelled Investigation stops at its next model call.
        cancellationToken.ThrowIfCancellationRequested();
        Calls.Add(([.. messages], options));
        return Task.FromResult(_script.Dequeue()());
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var update in (await GetResponseAsync(messages, options, cancellationToken)).ToChatResponseUpdates())
            yield return update;
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose() { }
}
