using System.Text.Json.Serialization;

namespace Vigil.Agents;

// Each enum is written by name ("High", not 2), so the model sees readable choices and a stored Finding stays readable.

[JsonConverter(typeof(JsonStringEnumConverter<Verdict>))]
public enum Verdict
{
    LikelyAccountTakeover,
    LikelyRegistrationFraud,
    Inconclusive,
    LikelyGenuine,
}

[JsonConverter(typeof(JsonStringEnumConverter<Confidence>))]
public enum Confidence
{
    Low,
    Medium,
    High,
}

[JsonConverter(typeof(JsonStringEnumConverter<Recommendation>))]
public enum Recommendation
{
    BlockBankChanges,
    ContactHolder,
    NoAction,
}

/// <summary>
/// The Investigator's written conclusion on a Case. It only recommends; the analyst decides.
/// </summary>
/// <param name="Events">The numbers of the events the summary relies on, such as [3, 4], as shown in the Account's timeline.</param>
public sealed record Finding(
    Verdict Verdict,
    Confidence Confidence,
    string Summary,
    Recommendation Recommendation,
    IReadOnlyList<int> Events);
