using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Vigil.Evaluation.Tests;

/// <summary>
/// Plays the Investigator's replies in order and approves or rejects every Review, each call costing 1,000 input and
/// 100 output tokens. No test calls a real LLM.
/// </summary>
public sealed class ScriptedChatClient : IChatClient
{
    private readonly Queue<Func<string>> _investigator = new();
    private bool _approve = true;

    public ScriptedChatClient Writes(string finding)
    {
        _investigator.Enqueue(() => finding);
        return this;
    }

    public ScriptedChatClient Fails(Exception exception)
    {
        _investigator.Enqueue(() => throw exception);
        return this;
    }

    public ScriptedChatClient RejectsEveryReview()
    {
        _approve = false;
        return this;
    }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        // The Reviewer is asked for a Review, a JSON schema with an "approved" property; the Investigator is not.
        var isReviewer = options?.ResponseFormat is ChatResponseFormatJson { Schema: { } schema }
            && schema.TryGetProperty("properties", out var properties) && properties.TryGetProperty("approved", out _);
        var reply = isReviewer
            ? (_approve ? """{"approved":true,"reasons":[]}""" : """{"approved":false,"reasons":["The summary overstates the evidence."]}""")
            : _investigator.Dequeue()();

        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, reply))
        {
            Usage = new UsageDetails { InputTokenCount = 1000, OutputTokenCount = 100 },
        });
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
