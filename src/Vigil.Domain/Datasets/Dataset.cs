using System.Text.Json;
using System.Text.Json.Serialization;
using Vigil.Domain.Events;

namespace Vigil.Domain.Datasets;

/// <summary>The Section 10.1 templates the simulator generates Accounts from.</summary>
public enum Persona
{
    QuietHolder,
    HouseMover,
    NewPhone,
    ActiveTrader,
    TravellingHolder,
    AtoFraudster,
    RegistrationFraudster,
    FraudRing,
    SlowBurnFraudster,
}

public enum FraudKind
{
    Ato,
    Registration,
    Ring,
    Evasion,
}

/// <summary>The ground truth for one Account. Evaluation reads it; it never goes to the API.</summary>
/// <param name="FraudKind">Null for a genuine Account.</param>
public sealed record Label(Persona Persona, FraudKind? FraudKind)
{
    [JsonIgnore]
    public bool IsFraud => FraudKind is not null;
}

public sealed record LabelledAccount(string AccountId, Label Label, IReadOnlyList<PortalEvent> Events);

public static class Dataset
{
    public static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public static void Write(IEnumerable<LabelledAccount> accounts, Stream stream)
    {
        foreach (var account in accounts)
        {
            JsonSerializer.Serialize(stream, account, JsonOptions);
            // A fixed "\n" rather than Environment.NewLine, so the same seed is byte-identical on every OS.
            stream.WriteByte((byte)'\n');
        }
    }

    public static IEnumerable<LabelledAccount> Read(Stream stream)
    {
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0) continue;
            yield return JsonSerializer.Deserialize<LabelledAccount>(line, JsonOptions)
                ?? throw new InvalidDataException("A Dataset line is null.");
        }
    }
}
