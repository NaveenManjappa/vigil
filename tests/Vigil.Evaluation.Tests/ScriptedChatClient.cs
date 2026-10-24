using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Vigil.Evaluation.Tests;

/// <summary>
/// Plays the Investigator's replies in order, approves or rejects every Review, and gives the Judge its scripted
/// judgements in order (all 4s once they run out), each call costing 1,000 input and 100 output tokens. No test calls
/// a real LLM.
/// </summary>
public sealed class ScriptedChatClient : IChatClient
{
    private readonly Queue<Func<string>> _investigator = new();
    private readonly Queue<Func<string>> _judge = new();
    private bool _approve = true;

    /// <summary>The message each Judge call was sent, in order.</summary>
    public List<string> JudgeMessages { get; } = [];

    public ScriptedChatClient Judges(string judgement)
    {
        _judge.Enqueue(() => judgement);
        return this;
    }

    public ScriptedChatClient JudgeFails(Exception exception)
    {
        _judge.Enqueue(() => throw exception);
        return this;
    }

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
        // Each agent is told apart by the JSON schema it asks for: a Review has "approved", a Judgement
        // "groundedClaims"; the Investigator asks for neither.
        string reply;
        if (Asks(options, "groundedClaims"))
        {
            JudgeMessages.Add(messages.Last(m => m.Role == ChatRole.User).Text);
            reply = _judge.Count > 0 ? _judge.Dequeue()() : AgentSamples.Judgement(4, 4, 4, 4);
        }
        else if (Asks(options, "approved"))
            reply = _approve ? """{"approved":true,"reasons":[]}""" : """{"approved":false,"reasons":["The summary overstates the evidence."]}""";
        else
            reply = _investigator.Dequeue()();

        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, reply))
        {
            Usage = new UsageDetails { InputTokenCount = 1000, OutputTokenCount = 100 },
        });
    }

    private static bool Asks(ChatOptions? options, string property) =>
        options?.ResponseFormat is ChatResponseFormatJson { Schema: { } schema }
        && schema.TryGetProperty("properties", out var properties) && properties.TryGetProperty(property, out _);

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
