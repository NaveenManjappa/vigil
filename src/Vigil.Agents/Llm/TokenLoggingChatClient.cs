using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Vigil.Agents;

/// <summary>
/// Logs the tokens and estimated cost of every LLM round trip. It sits below the agents, so a tool loop logs one line
/// per call to the model, which is what the Azure cost view bills.
/// </summary>
public sealed partial class TokenLoggingChatClient(IChatClient inner, LlmOptions prices, ILogger<TokenLoggingChatClient> logger)
    : DelegatingChatClient(inner)
{
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var response = await base.GetResponseAsync(messages, options, cancellationToken);
        Log(response);
        return response;
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Usage arrives in the last updates, so the count is only known once the stream has been read to the end.
        List<ChatResponseUpdate> updates = [];
        await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken))
        {
            updates.Add(update);
            yield return update;
        }
        Log(updates.ToChatResponse());
    }

    private void Log(ChatResponse response)
    {
        var input = response.Usage?.InputTokenCount ?? 0;
        var output = response.Usage?.OutputTokenCount ?? 0;
        LogTokens(response.ModelId ?? "unknown", input, output, prices.CostOf(input, output));
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "LLM call to {Model}: {InputTokens} input tokens, {OutputTokens} output tokens, estimated £{EstimatedCost}")]
    private partial void LogTokens(string model, long inputTokens, long outputTokens, decimal estimatedCost);
}
