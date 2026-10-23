using System.Text;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Vigil.Agents;

/// <summary>The Reviewer's answer: approve, or reject with reasons the Investigator retries with.</summary>
public sealed record Review(bool Approved, IReadOnlyList<string> Reasons);

public sealed record ReviewerResult(Review Review, long InputTokens, long OutputTokens, string? Model = null);

/// <summary>
/// The Reviewer agent: checks a Finding's claims against the evidence lines of the refs it cites. It has no tools, so
/// it judges only what the Investigator cited and cannot go looking for support the Finding never gave.
/// </summary>
public sealed class Reviewer
{
    // Stable on purpose, like the Investigator's: MAF checkpoints and telemetry identify the agent by them.
    public const string AgentId = "vigil-reviewer";
    public const string AgentName = "Reviewer";
    public const string PromptVersion = "reviewer.v1";

    private readonly AIAgent _agent;
    private readonly IChatClient _chat;

    public Reviewer(IChatClient chat)
    {
        _chat = chat;
        _agent = ChatAgents.Create(chat, AgentId, AgentName, PromptVersion);
    }

    /// <summary>
    /// Reviews one Finding that has passed validation. An LLM failure or a reply that is not a Review throws
    /// (<see cref="JsonException"/> for the latter), for the caller to turn into InvestigationFailed.
    /// </summary>
    public async Task<ReviewerResult> ReviewAsync(Finding finding, MaskedEvidence evidence, CancellationToken ct)
    {
        var response = await _agent.RunAsync<Review>(
            Message(finding, evidence), serializerOptions: ChatAgents.Json, cancellationToken: ct);

        var review = response.Result;
        return new ReviewerResult(
            review with { Reasons = review.Reasons ?? [] },
            response.Usage?.InputTokenCount ?? 0, response.Usage?.OutputTokenCount ?? 0, ChatAgents.ModelOf(response, _chat));
    }

    private static string Message(Finding finding, MaskedEvidence evidence)
    {
        // The summary was written by a model that only saw Pseudonyms, but it is masked again: every string to the
        // LLM goes through the masker, so a value the model guessed or echoed cannot reach a second prompt.
        var masked = finding with { Summary = evidence.Mask(finding.Summary) };

        var message = new StringBuilder()
            .AppendLine("Review this Finding:")
            .AppendLine(JsonSerializer.Serialize(masked, ChatAgents.Json))
            .AppendLine()
            .AppendLine("Evidence for the cited refs:");
        foreach (var line in evidence.LinesFor(finding.EvidenceRefs))
            message.AppendLine(line);
        return message.ToString();
    }
}
