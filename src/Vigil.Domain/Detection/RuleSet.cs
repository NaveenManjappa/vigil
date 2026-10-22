using System.Text.Json;
using System.Text.Json.Serialization;
using Vigil.Domain.Events;

namespace Vigil.Domain.Detection;

public enum RuleKind
{
    Sequence,
}

/// <summary>
/// One detection pattern from the rules file. A sequence rule has two steps, A then B,
/// and each step lists the event types that count as that step.
/// </summary>
/// <param name="Explanation">Template; {elapsed} and {window} are filled in when the rule hits.</param>
public sealed record Rule(
    string Id,
    string Name,
    RuleKind Kind,
    IReadOnlyList<IReadOnlyList<EventType>> Steps,
    int WindowMinutes,
    int Points,
    string Explanation)
{
    [JsonIgnore]
    public TimeSpan Window => TimeSpan.FromMinutes(WindowMinutes);
}

/// <summary>Lowest Score of each band; anything below Medium is Low.</summary>
public sealed record BandThresholds(int Medium, int High, int Critical)
{
    public RiskBand BandFor(int score) =>
        score >= Critical ? RiskBand.Critical
        : score >= High ? RiskBand.High
        : score >= Medium ? RiskBand.Medium
        : RiskBand.Low;
}

/// <summary>
/// Everything detection is configured by, loaded from a versioned JSON file (ADR-0002).
/// The version is stamped on every Alert so an auditor knows which rules were in force.
/// </summary>
public sealed record RuleSet(string Version, int RiskPeriodDays, BandThresholds Bands, IReadOnlyList<Rule> Rules)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        RespectRequiredConstructorParameters = true,
    };

    public static RuleSet Parse(string json)
    {
        var rules = JsonSerializer.Deserialize<RuleSet>(json, Json)
            ?? throw new InvalidDataException("Rules file is empty.");

        // Fail at startup, not on the first event that happens to reach a malformed rule.
        foreach (var rule in rules.Rules)
        {
            if (rule.Kind == RuleKind.Sequence && rule.Steps.Count != 2)
                throw new InvalidDataException($"Rule {rule.Id}: a sequence rule needs exactly two steps.");
        }

        return rules;
    }

    public static RuleSet Load(string path) => Parse(File.ReadAllText(path));
}
