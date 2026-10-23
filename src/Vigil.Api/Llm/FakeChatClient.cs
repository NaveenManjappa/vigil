using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Vigil.Api.Llm;

/// <summary>
/// The free provider for Development, tests and CI: answers every call with a fixed reply and token counts, so the
/// pipeline and its token logging behave as with a real model. Stage 3.4 replaces the reply with the scripted
/// Investigator and Reviewer.
/// </summary>
public sealed class FakeChatClient : IChatClient
{
    public const string ProviderName = "fake";
    public const string ModelId = "fake-model";

    private static readonly ChatClientMetadata Metadata = new(ProviderName, providerUri: null, defaultModelId: ModelId);

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Fake provider: no model was called."))
        {
            ModelId = ModelId,
            Usage = new UsageDetails { InputTokenCount = 0, OutputTokenCount = 0, TotalTokenCount = 0 },
        });

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var update in (await GetResponseAsync(messages, options, cancellationToken)).ToChatResponseUpdates())
            yield return update;
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is not null ? null
        : serviceType == typeof(ChatClientMetadata) ? Metadata
        : serviceType.IsInstanceOfType(this) ? this
        : null;

    public void Dispose() { }
}
