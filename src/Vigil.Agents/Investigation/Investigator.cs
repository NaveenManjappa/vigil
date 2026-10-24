using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Vigil.Agents;

/// <param name="InputTokens">Summed over every round trip of the tool loop, which is what is billed.</param>
/// <param name="Model">The model that answered, for the Audit Entry; null if the provider does not say.</param>
public sealed record InvestigatorResult(Finding Finding, long InputTokens, long OutputTokens, string? Model = null);

/// <summary>
/// The Investigator agent: given a Case's Enrichment it calls its tools as it sees fit and writes a Finding. One call
/// is one attempt; the workflow decides whether to retry with the Reviewer's reasons.
/// </summary>
public sealed class Investigator
{
    // Stable on purpose: MAF checkpoints and telemetry identify the agent by them, so renaming one strands every
    // Case already waiting in AwaitingApproval.
    public const string AgentId = "vigil-investigator";
    public const string AgentName = "Investigator";
    public const string PromptVersion = "investigator.v1";

    private readonly AIAgent _agent;
    private readonly IInvestigationSource _source;
    private readonly IChatClient _chat;

    public Investigator(IChatClient chat, IInvestigationSource source)
    {
        _source = source;
        _chat = chat;
        _agent = ChatAgents.Create(chat, AgentId, AgentName, PromptVersion);
    }

    /// <summary>
    /// Runs one attempt. An LLM failure or a reply that is not a Finding throws (<see cref="JsonException"/> for the
    /// latter), for the workflow to turn into InvestigationFailed.
    /// </summary>
    public async Task<InvestigatorResult> InvestigateAsync(
        Enrichment enrichment, IReadOnlyList<string> reviewerReasons, CancellationToken ct)
    {
        // Tools are given per run, not to the agent, because each Investigation binds them to its own Account.
        var tools = new InvestigatorTools(enrichment, _source);
        var options = new ChatClientAgentRunOptions(new ChatOptions { Tools = tools.AsAITools() });

        var response = await _agent.RunAsync<Finding>(
            Message(enrichment, reviewerReasons), serializerOptions: ChatAgents.Json, options: options, cancellationToken: ct);

        // Result parses lazily, so it is read here, where a malformed reply should fail.
        return new InvestigatorResult(
            response.Result, response.Usage?.InputTokenCount ?? 0, response.Usage?.OutputTokenCount ?? 0, ChatAgents.ModelOf(response, _chat));
    }

    private static string Message(Enrichment enrichment, IReadOnlyList<string> reviewerReasons)
    {
        if (reviewerReasons.Count == 0)
            return enrichment.Brief;

        // The Reviewer only ever saw masked text, but the reasons are masked again: every string to the LLM goes through the masker.
        var reasons = string.Join('\n', reviewerReasons.Select(r => "- " + enrichment.Evidence.Mask(r)));
        return $"{enrichment.Brief}\nThe Reviewer rejected your previous Finding for these reasons:\n{reasons}\nWrite a new Finding that addresses each one.";
    }
}
