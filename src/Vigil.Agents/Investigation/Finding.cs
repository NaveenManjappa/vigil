using System.Text.Json;

namespace Vigil.Agents;

public enum Verdict
{
    LikelyAccountTakeover,
    LikelyRegistrationFraud,
    Inconclusive,
    LikelyGenuine,
}

public enum Confidence
{
    Low,
    Medium,
    High,
}

public enum Recommendation
{
    BlockBankChanges,
    ContactHolder,
    NoAction,
}

/// <summary>
/// The Investigator's written conclusion on a Case, as the model returns it: masked, citing Evidence Refs rather
/// than EventIds. It only recommends; the analyst decides.
/// </summary>
/// <param name="Summary">At most 120 words; checked in code before review, not trusted from the prompt.</param>
public sealed record Finding(
    Verdict Verdict,
    Confidence Confidence,
    string Summary,
    Recommendation Recommendation,
    IReadOnlyList<string> Evidence)
{
    /// <summary>The Finding as stored with its Case: the shape the model writes, enums by name.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, Investigator.Json);

    public static Finding FromJson(string json) =>
        JsonSerializer.Deserialize<Finding>(json, Investigator.Json) ?? throw new JsonException("A stored Finding was null.");
}
