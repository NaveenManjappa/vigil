using System.Text.Json;
using System.Text.Json.Serialization;
using Vigil.Domain.Events;

namespace Vigil.Domain.Detection;

public enum RuleKind
{
    Sequence,
    FirstSighting,
    Graph,
}

/// <summary>
/// One detection pattern from the rules file. A sequence rule has two steps, A then B, and each step lists the event
/// types that count as that step. A first-sighting rule and a graph rule have no steps and no Window.
/// </summary>
/// <param name="Explanation">
/// Template: a sequence rule fills in {elapsed} and {window}, a first-sighting rule fills in {sighting}, G1 fills in
/// {bankAccount} and {account}, and G2 fills in {identifier}, {value} and {account}.
/// </param>
public sealed record Rule(
    string Id,
    string Name,
    RuleKind Kind,
    int Points,
    string Explanation)
{
    public IReadOnlyList<IReadOnlyList<EventType>> Steps { get; init; } = [];

    public int WindowMinutes { get; init; }

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
/// Everything detection is configured by, loaded from <c>config/rules.json</c> (ADR-0002). The version is stamped on
/// every Alert so an auditor knows which rules were in force.
/// </summary>
/// <param name="RiskPeriodDays">An Alert whose last hit is older than this, in event time, is closed and a new one starts.</param>
/// <param name="OpenCaseAt">The band at which an Alert gets a Case, so an analyst and the agent look at it.</param>
public sealed record RuleSet(
    string Version, int RiskPeriodDays, BandThresholds Bands, RiskBand OpenCaseAt, IReadOnlyList<Rule> Rules)
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
        foreach (var rule in rules.Rules.Where(r => r.Kind == RuleKind.Sequence))
        {
            if (rule.Steps.Count != 2)
                throw new InvalidDataException($"Rule {rule.Id}: a sequence rule needs exactly two steps.");
            if (rule.WindowMinutes <= 0)
                throw new InvalidDataException($"Rule {rule.Id}: a sequence rule needs a positive windowMinutes.");
        }

        return rules;
    }

    public static RuleSet Load(string path) => Parse(File.ReadAllText(path));
}
