using System.Text;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Vigil.Agents;
using Vigil.Domain.Events;
using Vigil.Simulator.Scenarios;

namespace Vigil.Evaluation;

/// <param name="InputTokens">What the one call was billed, for the console and the budget.</param>
public sealed record GeneratorRun(GenerationResult Result, long InputTokens, long OutputTokens);

/// <summary>
/// The Scenario Generator agent: a red team that reads the rules and writes labelled Generated Scenarios meant to slip
/// past them. One call asks for a batch; <see cref="GeneratedScenarios.Accept"/> decides which are kept.
/// </summary>
public sealed class ScenarioGenerator
{
    public const string AgentId = "vigil-scenario-generator";
    public const string AgentName = "ScenarioGenerator";
    public const string PromptVersion = "scenario-generator.v1";

    /// <summary>The most one run may ask for, so a mistyped --count cannot spend more than one small call (ADR-0016).</summary>
    public const int MaxCount = 10;

    // Only the outer shape is enforced by the model; each Scenario is checked by the strict parser instead, because a
    // schema for the Scenario file would need open dictionaries that strict structured output does not allow.
    // The Fake provider also recognises the generator by this "scenarios" property.
    private static readonly ChatResponseFormat ReplyFormat = ChatResponseFormat.ForJsonSchema(
        JsonDocument.Parse("""
            {
              "type": "object",
              "properties": { "scenarios": { "type": "array", "items": { "type": "object" } } },
              "required": ["scenarios"]
            }
            """).RootElement,
        schemaName: "GeneratedScenarios");

    private readonly AIAgent _agent;

    public ScenarioGenerator(IChatClient chat)
    {
        // No Temperature 0, unlike the Investigator: a red team that always writes the same journey finds one gap.
        _agent = chat.AsAIAgent(new ChatClientAgentOptions
        {
            Id = AgentId,
            Name = AgentName,
            ChatOptions = new ChatOptions { Instructions = Prompts.Load(PromptVersion) },
        });
    }

    /// <param name="rulesJson">The rules file as written, so the model reads the same Windows and points detection uses.</param>
    /// <param name="examples">Scenario files to copy the format from.</param>
    /// <param name="existing">Every Scenario already kept; their shapes are listed so the model does not repeat them.</param>
    public async Task<GeneratorRun> GenerateAsync(
        int count, string rulesJson, IReadOnlyList<string> examples, IReadOnlyList<Scenario> existing, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, MaxCount);

        var options = new ChatClientAgentRunOptions(new ChatOptions { ResponseFormat = ReplyFormat });
        var response = await _agent.RunAsync(Message(count, rulesJson, examples, existing), options: options, cancellationToken: ct);

        return new GeneratorRun(
            GeneratedScenarios.Accept(response.Text, existing),
            response.Usage?.InputTokenCount ?? 0, response.Usage?.OutputTokenCount ?? 0);
    }

    private static string Message(int count, string rulesJson, IReadOnlyList<string> examples, IReadOnlyList<Scenario> existing)
    {
        var message = new StringBuilder()
            .AppendLine($"Write {count} Generated Scenarios of fraud the rules would miss.")
            .AppendLine()
            .AppendLine("Rules file:")
            .AppendLine(rulesJson)
            .AppendLine()
            .AppendLine($"Event types: {string.Join(", ", Enum.GetNames<EventType>())}.")
            .AppendLine();
        foreach (var (example, i) in examples.Select((e, i) => (e, i + 1)))
            message.AppendLine($"Example Scenario {i}:").AppendLine(example).AppendLine();

        message.AppendLine("Shapes already written, not to repeat:");
        foreach (var scenario in existing)
            message.AppendLine($"- {GeneratedScenarios.Signature(scenario)}");
        if (existing.Count == 0)
            message.AppendLine("- none yet");
        return message.ToString();
    }
}
