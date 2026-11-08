using System.Text;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Vigil.Domain.Detection;
using Vigil.Domain.Events;

namespace Vigil.Agents;

/// <summary>What one Investigation produced, with the tokens it cost.</summary>
public sealed record InvestigationResult(Finding Finding, string? Model, long InputTokens, long OutputTokens);

/// <summary>
/// The Investigator agent. It reads a Case's Alert, calls its tools as it sees fit and writes a Finding.
/// </summary>
public sealed class Investigator(IChatClient chat, IAccountData data)
{
    public async Task<InvestigationResult> InvestigateAsync(Alert alert, CancellationToken ct)
    {
        // 1. Read the Account's events, so the brief can show the ones the Rules flagged, by number.
        var timeline = await data.GetTimelineAsync(alert.AccountId, ct);

        // 2. Build the agent: a chat model, standing instructions and three tools it may call.
        var tools = new InvestigatorTools(alert.AccountId, alert.Hits.Min(h => h.TriggeredAt), data);
        AIAgent agent = chat.AsAIAgent(name: "Investigator", instructions: Instructions, tools: tools.All());

        // 3. Run it. RunAsync<Finding> asks the model for JSON in the Finding's shape and parses the reply.
        //    Behind this one call, the agent may call tools and send their results back to the model several times.
        var response = await agent.RunAsync<Finding>(Brief(alert, timeline), cancellationToken: ct);

        var model = chat.GetService<ChatClientMetadata>()?.DefaultModelId;
        return new InvestigationResult(response.Result, model, response.Usage?.InputTokenCount ?? 0, response.Usage?.OutputTokenCount ?? 0);
    }

    /// <summary>The first message: the Alert's Rule Hits and the events behind them.</summary>
    private static string Brief(Alert alert, IReadOnlyList<PortalEvent> timeline)
    {
        var numberOf = timeline.Select((e, i) => (e.EventId, Number: i + 1)).ToDictionary(x => x.EventId, x => x.Number);

        var brief = new StringBuilder()
            .AppendLine($"Investigate Account {alert.AccountId}. Alert Score {alert.Score}, Risk Band {alert.Band}.")
            .AppendLine()
            .AppendLine("Rule Hits:");
        foreach (var hit in alert.Hits)
        {
            var events = string.Join(", ", hit.EvidenceEventIds.Select(id => $"#{numberOf[id]}"));
            brief.AppendLine($"- {hit.RuleId} (+{hit.Points}): {hit.Explanation}. Events: {events}");
        }

        brief.AppendLine().AppendLine("The flagged events:");
        var flagged = alert.Hits.SelectMany(h => h.EvidenceEventIds).Distinct().OrderBy(id => numberOf[id]);
        foreach (var id in flagged)
            brief.AppendLine(InvestigatorTools.Describe(numberOf[id], timeline[numberOf[id] - 1]));

        return brief.ToString();
    }

    // The standing instructions, from config/prompts/investigator.md, embedded in this assembly.
    private static readonly string Instructions = ReadPrompt("investigator.md");

    private static string ReadPrompt(string name)
    {
        using var stream = typeof(Investigator).Assembly.GetManifestResourceStream($"prompts.{name}")
            ?? throw new InvalidOperationException($"No prompt file config/prompts/{name} is embedded.");
        return new StreamReader(stream).ReadToEnd();
    }
}
