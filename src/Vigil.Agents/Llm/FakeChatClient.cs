using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;

namespace Vigil.Agents;

/// <summary>
/// A free stand-in for a real model, for Development, tests and CI. It plays a scripted Investigator: first it calls the
/// GetLinkedAccounts tool, then it answers with a fixed Finding that cites every event number in the brief. So the free
/// walkthrough still shows a tool call in the trace, and nothing is sent to a real model.
/// </summary>
public sealed partial class FakeChatClient : IChatClient
{
    public const string ProviderName = "fake";
    public const string ModelId = "fake-model";

    private static readonly ChatClientMetadata Metadata = new(ProviderName, providerUri: null, defaultModelId: ModelId);

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var history = messages.ToList();
        var hasTools = options?.Tools?.Count > 0;
        var toolAlreadyCalled = history.SelectMany(m => m.Contents).OfType<FunctionResultContent>().Any();

        var reply = !hasTools ? new ChatMessage(ChatRole.Assistant, "Fake provider: no model was called.")
            : !toolAlreadyCalled ? new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("fake-call-1", "GetLinkedAccounts")])
            : new ChatMessage(ChatRole.Assistant, Finding(history));

        return Task.FromResult(new ChatResponse(reply)
        {
            ModelId = ModelId,
            Usage = new UsageDetails { InputTokenCount = 0, OutputTokenCount = 0, TotalTokenCount = 0 },
        });
    }

    private static string Finding(List<ChatMessage> history)
    {
        var brief = history.First(m => m.Role == ChatRole.User).Text;
        var events = EventNumber().Matches(brief).Select(m => int.Parse(m.Groups[1].Value)).Distinct().Order();

        return JsonSerializer.Serialize(new
        {
            verdict = "LikelyAccountTakeover",
            confidence = "High",
            summary = "Fake provider, no model was called: a scripted Finding that cites every flagged event.",
            recommendation = "BlockBankChanges",
            events,
        });
    }

    [GeneratedRegex(@"#(\d+)")]
    private static partial Regex EventNumber();

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var update in (await GetResponseAsync(messages, options, cancellationToken)).ToChatResponseUpdates())
            yield return update;
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType == typeof(ChatClientMetadata) ? Metadata
        : serviceKey is null && serviceType.IsInstanceOfType(this) ? this
        : null;

    public void Dispose() { }
}
