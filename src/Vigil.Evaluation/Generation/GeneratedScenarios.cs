using System.Text.Json;
using System.Text.RegularExpressions;
using Vigil.Simulator.Scenarios;

namespace Vigil.Evaluation;

/// <param name="Json">The Scenario file to write, exactly as the generator described it, indented for review.</param>
public sealed record KeptScenario(Scenario Scenario, string Json);

/// <param name="Rejected">One reason per Scenario not kept, printed so a poor prompt version is visible.</param>
public sealed record GenerationResult(IReadOnlyList<KeptScenario> Kept, IReadOnlyList<string> Rejected);

/// <summary>
/// The rules a Generated Scenario must pass to be kept: it parses as a Scenario, it carries a Label, and its
/// signature is unlike every Scenario already kept.
/// </summary>
public static partial class GeneratedScenarios
{
    // Relaxed escaping, so an apostrophe in an intent reads as one when the file is reviewed; it is never put in HTML.
    private static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// Sorts the generator's reply, a <c>{ "scenarios": [...] }</c> object, into Scenarios to keep and reasons for the
    /// rest. A reply of the wrong shape throws, since nothing in it can be trusted.
    /// </summary>
    /// <param name="existing">Every Scenario already kept, which a new one must differ from in name and signature.</param>
    public static GenerationResult Accept(string reply, IEnumerable<Scenario> existing)
    {
        var candidates = Candidates(reply);
        // Hand-written Scenarios may share a shape (two D-77 resets), so the first name stands for it.
        Dictionary<string, string> bySignature = [];
        HashSet<string> names = [];
        foreach (var scenario in existing)
        {
            bySignature.TryAdd(Signature(scenario), scenario.Name);
            names.Add(scenario.Name);
        }
        List<KeptScenario> kept = [];
        List<string> rejected = [];

        foreach (var (candidate, i) in candidates.Select((c, i) => (c, i + 1)))
        {
            Scenario scenario;
            try
            {
                scenario = Scenario.Parse(candidate.GetRawText(), $"generated scenario {i}");
            }
            catch (InvalidDataException e)
            {
                rejected.Add(e.Message);
                continue;
            }

            var problem = scenario.Label is null ? "has no label, which every Generated Scenario needs"
                // The name becomes the file name, so anything else could write outside datasets/generated/.
                : !FileName().IsMatch(scenario.Name) ? "name must be lower-case words joined by hyphens"
                : names.Contains(scenario.Name) ? "name is already taken"
                : bySignature.TryGetValue(Signature(scenario), out var twin) ? $"has the same shape as \"{twin}\""
                : null;
            if (problem is not null)
            {
                rejected.Add($"generated scenario {i} (\"{scenario.Name}\"): {problem}.");
                continue;
            }

            bySignature[Signature(scenario)] = scenario.Name;
            names.Add(scenario.Name);
            kept.Add(new KeptScenario(scenario, JsonSerializer.Serialize(candidate, Indented)));
        }

        return new GenerationResult(kept, rejected);
    }

    /// <summary>
    /// Every Generated Scenario kept so far. A file without a Label throws rather than being skipped: a hand edit that
    /// dropped it would otherwise quietly remove a Blind Spot from the report.
    /// </summary>
    public static IReadOnlyList<Scenario> LoadAll(string folder)
    {
        if (!Directory.Exists(folder)) return [];
        var scenarios = Scenario.LoadAll(folder);
        if (scenarios.FirstOrDefault(s => s.Label is null) is { } unlabelled)
            throw new InvalidDataException($"{folder}: \"{unlabelled.Name}\" has no label, which every Generated Scenario needs.");
        return scenarios;
    }

    /// <summary>
    /// The journey's shape: its event types in order, with the gaps between them rounded to the hour. Names,
    /// devices and minutes are left out, so a model re-wording the same journey does not count as a new one.
    /// </summary>
    public static string Signature(Scenario scenario)
    {
        var steps = scenario.Steps.Select(s => s.Event).OrderBy(e => e.OccurredAt).ToList();
        return string.Join(" ", steps.Select((e, i) => i == 0
            ? $"{e.Type}"
            : $"+{Math.Round((e.OccurredAt - steps[i - 1].OccurredAt).TotalHours, MidpointRounding.AwayFromZero)}h {e.Type}"));
    }

    private static List<JsonElement> Candidates(string reply)
    {
        try
        {
            using var document = JsonDocument.Parse(reply);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("scenarios", out var scenarios)
                || scenarios.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("The generator's reply has no \"scenarios\" array.");
            // Cloned, because each element outlives the document it was read from.
            return scenarios.EnumerateArray().Select(e => e.Clone()).ToList();
        }
        catch (JsonException e)
        {
            throw new InvalidDataException($"The generator's reply is not JSON: {e.Message}", e);
        }
    }

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex FileName();
}
