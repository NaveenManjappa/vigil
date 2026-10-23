using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Vigil.Domain.Telemetry;

namespace Vigil.Agents;

/// <summary>What the Investigator and the Reviewer share: how they are built, how their JSON reads, and who answered.</summary>
internal static class ChatAgents
{
    /// <summary>
    /// One shape for every Finding and Review, whether the model writes it, a prompt shows it, a Case stores it or a
    /// checkpoint holds it: enums by name, so a stored value still reads after the enum is reordered.
    /// </summary>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    public static AIAgent Create(IChatClient chat, string id, string name, string promptVersion) =>
        chat.AsAIAgent(new ChatClientAgentOptions
        {
            Id = id,
            Name = name,
            ChatOptions = new ChatOptions { Instructions = Prompts.Load(promptVersion), Temperature = 0 },
        })
            // The invoke_agent span, tagged with the agent's Id, that its chat and tool spans nest under.
            .AsBuilder().UseOpenTelemetry(sourceName: VigilTelemetry.AgentsSourceName).Build();

    /// <summary>
    /// The model named on the reply, which is the one that actually answered, else the provider's default. The chat
    /// client's reply sits under the structured-output wrapper, one raw representation down per layer.
    /// </summary>
    public static string? ModelOf(AgentResponse response, IChatClient chat)
    {
        object? raw = response;
        while (raw is AgentResponse layer) raw = layer.RawRepresentation;
        return (raw as ChatResponse)?.ModelId ?? chat.GetService<ChatClientMetadata>()?.DefaultModelId;
    }
}
