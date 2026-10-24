using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;

namespace Vigil.Agents;

/// <summary>
/// The free provider for Development, tests and CI. When it is given the Investigator's tools it plays a scripted
/// Investigator: one graph tool call, then a Finding citing the refs it was shown, so the free demo still shows tool
/// calling in the trace. When asked for a Review it plays a Reviewer that approves. Anything else gets a fixed reply.
/// </summary>
public sealed partial class FakeChatClient : IChatClient
{
    public const string ProviderName = "fake";
    public const string ModelId = "fake-model";

    private const string GraphTool = "QueryGraphNeighbourhood";

    private static readonly ChatClientMetadata Metadata = new(ProviderName, providerUri: null, defaultModelId: ModelId);

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var history = messages.ToList();
        var isInvestigator = options?.Tools?.Any(t => t.Name == GraphTool) == true;
        var reply = IsReviewer(options) ? new ChatMessage(ChatRole.Assistant, """{"approved":true,"reasons":[]}""")
            : !isInvestigator ? new ChatMessage(ChatRole.Assistant, "Fake provider: no model was called.")
            : history.SelectMany(m => m.Contents).OfType<FunctionResultContent>().Any() ? new ChatMessage(ChatRole.Assistant, Finding(history))
            : new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("fake-call-1", GraphTool)]);

        return Task.FromResult(new ChatResponse(reply)
        {
            ModelId = ModelId,
            Usage = new UsageDetails { InputTokenCount = 0, OutputTokenCount = 0, TotalTokenCount = 0 },
        });
    }

    // Recognised by the Review contract it is asked to fill, not by prompt wording, so a new prompt version still works.
    private static bool IsReviewer(ChatOptions? options) =>
        options?.ResponseFormat is ChatResponseFormatJson { Schema: { } schema }
        && schema.TryGetProperty("properties", out var properties)
        && properties.TryGetProperty("approved", out _);

    // Cites every E-ref in the brief and each graph fact that touches a Confirmed Fraud Account, so the Finding only
    // ever cites refs that exist and the code validation step passes.
    private static string Finding(List<ChatMessage> history)
    {
        var brief = history.First(m => m.Role == ChatRole.User).Text;
        var graphFacts = history.SelectMany(m => m.Contents).OfType<FunctionResultContent>()
            .SelectMany(r => (r.Result?.ToString() ?? "").Split('\n'))
            .Where(line => line.Contains("Confirmed Fraud"));
        var evidence = EvidenceRef().Matches(brief).Concat(graphFacts.Select(line => EvidenceRef().Match(line)))
            .Where(m => m.Success).Select(m => m.Value).Distinct().ToList();

        return JsonSerializer.Serialize(new
        {
            verdict = "LikelyAccountTakeover",
            confidence = "High",
            summary = $"Scripted Finding from the Fake provider: the activity on {AccountPseudonym().Match(brief).Value} came " +
                      "from a device and country the Holder does not use, and that device is shared with a Confirmed Fraud Account.",
            recommendation = "BlockBankChanges",
            evidence,
        });
    }

    [GeneratedRegex(@"\b[EG]-\d+\b")]
    private static partial Regex EvidenceRef();

    // Named in the summary so the analyst-facing read visibly unmasks it.
    [GeneratedRegex(@"ACCOUNT-\d+")]
    private static partial Regex AccountPseudonym();

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
