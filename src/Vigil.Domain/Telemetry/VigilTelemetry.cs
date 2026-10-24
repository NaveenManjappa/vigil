using System.Diagnostics;

namespace Vigil.Domain.Telemetry;

/// <summary>
/// Vigil's own telemetry names. Here, in the project every other one references, so detection, the graph and the
/// API share one source. ServiceDefaults registers the same names as strings, since it is used by the simulator too.
/// </summary>
public static class VigilTelemetry
{
    /// <summary>The ActivitySource and Meter for Vigil's own spans and metrics.</summary>
    public const string Name = "Vigil";

    /// <summary>Spans and GenAI metrics of each LLM round trip, from the chat client's OpenTelemetry layer.</summary>
    public const string LlmSourceName = "Vigil.Llm";

    /// <summary>Spans and GenAI metrics of each agent run, from the agents' OpenTelemetry layer.</summary>
    public const string AgentsSourceName = "Vigil.Agents";

    /// <summary>Microsoft Agent Framework's own name for workflow spans; it has no meter.</summary>
    public const string WorkflowSourceName = "Microsoft.Agents.AI.Workflows";

    // Without a listener StartActivity returns null and costs almost nothing, so evaluation replays pay nothing for it.
    public static readonly ActivitySource Source = new(Name);
}
