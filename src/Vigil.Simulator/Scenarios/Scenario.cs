using System.Text.Json;
using System.Text.RegularExpressions;
using Vigil.Domain.Events;
using Vigil.Domain.Fraud;

namespace Vigil.Simulator.Scenarios;

/// <param name="After">The offset as the file wrote it, e.g. "1d5h", so a replay shows the author's own words.</param>
public sealed record ScenarioStep(int Number, string After, string Actor, PortalEvent Event);

/// <summary>
/// A fixed, hand-scripted Account journey, read from a JSON file in scenarios/ so a new one can be written
/// and replayed without a code change. The same shape is what the Stage 4 Scenario Generator will write.
/// </summary>
public sealed partial class Scenario
{
    /// <summary>Start of every journey unless a file sets its own, so runs are reproducible (Mon 2026-09-21 09:00 UTC).</summary>
    public static readonly DateTimeOffset DefaultStart = new(2026, 9, 21, 9, 0, 0, TimeSpan.Zero);

    public const string Holder = "holder";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
        // A misspelt field would otherwise be dropped silently and the step would run with a default.
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };

    private Scenario(
        string name, string description, IReadOnlyList<PortalEvent> baseline, IReadOnlyList<ScenarioStep> steps, ConfirmedFraud? confirmedFraud)
    {
        Name = name;
        Description = description;
        Baseline = baseline;
        Steps = steps;
        ConfirmedFraud = confirmedFraud;
    }

    public string Name { get; }
    public string Description { get; }
    public IReadOnlyList<PortalEvent> Baseline { get; }
    public IReadOnlyList<ScenarioStep> Steps { get; }

    /// <summary>Set when the file gives a <c>confirmedFraud</c> decision date; the simulator seeds it after posting the events.</summary>
    public ConfirmedFraud? ConfirmedFraud { get; }

    public IReadOnlyList<PortalEvent> Events() => [.. Baseline, .. Steps.Select(s => s.Event)];

    public static Scenario Load(string path) => Parse(File.ReadAllText(path), path);

    /// <summary>
    /// Ordered by first event time, so history another Scenario links to is in the graph before it is posted, such as
    /// D-77's earlier Accounts before Margaret. Ties keep file-name order, so every machine posts the same sequence.
    /// </summary>
    public static IReadOnlyList<Scenario> LoadAll(string folder) =>
        Directory.GetFiles(folder, "*.json").Order(StringComparer.Ordinal).Select(Load)
            .OrderBy(s => s.Events().Min(e => e.OccurredAt)).ToList();

    /// <param name="source">Named in every error, so a typo in a hand-written file is found at once.</param>
    public static Scenario Parse(string json, string source)
    {
        ScenarioFile file;
        try
        {
            file = JsonSerializer.Deserialize<ScenarioFile>(json, Json) ?? throw new InvalidDataException($"{source}: empty file.");
        }
        catch (JsonException e)
        {
            throw new InvalidDataException($"{source}: {e.Message}", e);
        }

        // A journey with no steps has nothing to detect, and no first event to order it by.
        if (file.Steps.Count == 0) throw new InvalidDataException($"{source}: no steps.");

        var start = file.Start ?? DefaultStart;
        var holderId = file.HolderId ?? $"HOLDER-{file.AccountId}";
        var actors = new Dictionary<string, Actor>(file.Actors ?? []);
        if (file.Baseline is { } usual) actors.TryAdd(Holder, new Actor(usual.Device, usual.Ip, usual.Country));

        // Stable EventIds, so posting a scenario twice exercises ingestion idempotency instead of creating duplicates.
        var baseline = file.Baseline is { } b
            ? Enumerable.Range(1, b.Weeks).Select(week => new PortalEvent(
                DeterministicGuid.Create($"{file.Name}/baseline/{week}"), file.AccountId, holderId, EventType.LoginSucceeded,
                start.AddDays(-7 * (b.Weeks + 1 - week)), b.Device, b.Ip, b.Country, "web", new Dictionary<string, string>())).ToList()
            : [];

        var steps = file.Steps.Select((step, i) =>
        {
            var number = i + 1;
            InvalidDataException Error(string problem) => new($"{source}, step {number}: {problem}");

            // Enum.TryParse also accepts numbers ("3") and combinations, which would hide a typo behind another event type.
            if (int.TryParse(step.Type, out _) || !Enum.TryParse<EventType>(step.Type, out var type) || !Enum.IsDefined(type))
                throw Error($"unknown event type \"{step.Type}\".");
            var name = step.As ?? Holder;
            if (!actors.TryGetValue(name, out var actor))
                throw Error(name == Holder ? "no baseline, so the step needs an \"as\" actor." : $"unknown actor \"{name}\".");
            var after = Offset(step.After) ?? throw Error($"\"{step.After}\" is not an offset like 10m, 4h or 1d5h (field \"after\").");

            return new ScenarioStep(number, step.After, name, new PortalEvent(
                DeterministicGuid.Create($"{file.Name}/{number}"), file.AccountId, holderId, type, start + after,
                actor.Device, actor.Ip, actor.Country, actor.Channel, step.Attributes ?? []));
        }).ToList();

        var confirmedFraud = file.ConfirmedFraud is { } decidedAt
            ? new ConfirmedFraud(file.AccountId, decidedAt, ConfirmedFraudSource.Seed)
            : null;
        return new Scenario(file.Name, file.Description, baseline, steps, confirmedFraud);
    }

    private static TimeSpan? Offset(string text)
    {
        var match = OffsetPattern().Match(text);
        if (!match.Success || text.Length == 0) return null;
        int Part(string unit) => match.Groups[unit].Success ? int.Parse(match.Groups[unit].Value) : 0;
        return new TimeSpan(Part("d"), Part("h"), Part("m"), 0);
    }

    [GeneratedRegex(@"^(?:(?<d>\d+)d)?(?:(?<h>\d+)h)?(?:(?<m>\d+)m)?$")]
    private static partial Regex OffsetPattern();

    private sealed record ScenarioFile(
        string Name,
        string Description,
        string AccountId,
        IReadOnlyList<StepFile> Steps,
        string? HolderId = null,
        DateTimeOffset? Start = null,
        BaselineFile? Baseline = null,
        Dictionary<string, Actor>? Actors = null,
        DateTimeOffset? ConfirmedFraud = null);

    private sealed record BaselineFile(string Device, string Ip, string Country, int Weeks = 13);

    private sealed record Actor(string Device, string Ip, string Country, string Channel = "web");

    private sealed record StepFile(string After, string Type, string? As = null, Dictionary<string, string>? Attributes = null);
}
