using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Vigil.Agents;
using Vigil.Domain.Telemetry;

namespace Vigil.Evaluation;

/// <summary>One criterion's score, 1 (poor) to 5 (excellent), and the Judge's reason for it.</summary>
public sealed record JudgeScore(int Score, string Reason);

/// <summary>The Judge's scores on the four criteria of the <c>judge.v1</c> rubric.</summary>
public sealed record Judgement(
    JudgeScore GroundedClaims, JudgeScore SoundReasoning, JudgeScore ProportionateRecommendation, JudgeScore Clarity)
{
    public IReadOnlyList<JudgeScore> Scores => [GroundedClaims, SoundReasoning, ProportionateRecommendation, Clarity];
}

public sealed record JudgeResult(Judgement Judgement, long InputTokens, long OutputTokens);

/// <summary>
/// The evaluation-only Judge (ADR-0016): scores a Finding against the same masked evidence the Investigator saw. It
/// lives here rather than in Vigil.Agents because it never runs on a live Case, and it has no tools, so it scores what
/// was shown rather than what it could go and find.
/// </summary>
public sealed class Judge
{
    public const string AgentId = "vigil-judge";
    public const string AgentName = "Judge";
    public const string PromptVersion = "judge.v1";

    // Web defaults read the rubric's camelCase names; enums by name so the Finding shown reads as the Investigator wrote it.
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    private readonly AIAgent _agent;

    public Judge(IChatClient chat) =>
        // Built as the Investigator and Reviewer are, so a real run's trace shows the Judge beside them.
        _agent = chat.AsAIAgent(new ChatClientAgentOptions
        {
            Id = AgentId,
            Name = AgentName,
            ChatOptions = new ChatOptions { Instructions = Prompts.Load(PromptVersion), Temperature = 0 },
        }).AsBuilder().UseOpenTelemetry(sourceName: VigilTelemetry.AgentsSourceName).Build();

    /// <summary>
    /// Scores one Finding. An LLM failure throws, and so does a reply that is not a full Judgement on the 1 to 5 scale
    /// (<see cref="JsonException"/>), for the caller to count as unjudged rather than average a score it cannot trust.
    /// </summary>
    public async Task<JudgeResult> ScoreAsync(Finding finding, Enrichment enrichment, CancellationToken ct)
    {
        var response = await _agent.RunAsync<Judgement>(Message(finding, enrichment), serializerOptions: Json, cancellationToken: ct);

        var judgement = response.Result ?? throw new JsonException("The Judge returned no Judgement.");
        // Nulls slip past the record's non-nullable types when the model leaves a criterion out.
        if (judgement.Scores.Any(s => s is null || s.Score is < 1 or > 5 || string.IsNullOrWhiteSpace(s.Reason)))
            throw new JsonException("The Judge left a criterion out or scored it outside 1 to 5.");

        return new JudgeResult(judgement, response.Usage?.InputTokenCount ?? 0, response.Usage?.OutputTokenCount ?? 0);
    }

    private static string Message(Finding finding, Enrichment enrichment)
    {
        var evidence = enrichment.Evidence;
        // Masked again, as for the Reviewer: every string to the LLM goes through the masker.
        var masked = finding with { Summary = evidence.Mask(finding.Summary) };
        // Every line shown, cited or not, so the Judge can tell when the Finding ignored evidence against its verdict.
        var lines = evidence.LinesFor(evidence.Refs.Entries.Select(e => e.Key));

        var message = new StringBuilder()
            .AppendLine("Score this Finding:")
            .AppendLine(JsonSerializer.Serialize(masked, Json))
            .AppendLine()
            .AppendLine("The Investigator's brief:")
            .AppendLine(enrichment.Brief)
            .AppendLine("Every evidence line the Investigator was shown:");
        foreach (var line in lines)
            message.AppendLine(line);
        return message.ToString();
    }
}
