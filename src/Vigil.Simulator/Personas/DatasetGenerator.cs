using Vigil.Domain.Datasets;

namespace Vigil.Simulator.Personas;

/// <summary>
/// Generates a labelled Dataset from the Section 10.1 Personas. Every random choice comes from one
/// seeded generator, so the same seed and holder count always give the same Dataset.
/// </summary>
public static class DatasetGenerator
{
    public static readonly DateTimeOffset ClockStart = new(2026, 6, 23, 0, 0, 0, TimeSpan.Zero);

    public static readonly TimeSpan ClockSpan = TimeSpan.FromDays(90);

    private const double FraudShare = 0.03;

    // Enough Travelling holders that the false positives they raise show up in every report, not by chance.
    private const double TravellerShare = 0.02;
    public const int RingSize = 5;

    // One ring plus one each of the other three fraud Personas, so every Persona appears even in a small Dataset.
    private const int MinimumFraud = RingSize + 3;
    private const int MinimumGenuine = 5;

    public static IReadOnlyList<LabelledAccount> Generate(int seed, int holders)
    {
        var fraud = Math.Max(MinimumFraud, (int)Math.Round(holders * FraudShare));
        ArgumentOutOfRangeException.ThrowIfLessThan(holders, fraud + MinimumGenuine);

        var rng = new Random(seed);
        var name = $"dataset-{seed}-{holders}";

        var travellers = Math.Max(1, (int)Math.Round(holders * TravellerShare));
        var slots = FraudSlots(fraud).Concat(GenuineSlots(holders - fraud, travellers, rng)).ToArray();
        rng.Shuffle(slots);

        return slots
            .Select((slot, i) => new AccountBuilder(name, i + 1, rng).Build(slot.Persona, slot.Ring))
            .ToList();
    }

    private static IEnumerable<(Persona Persona, int Ring)> FraudSlots(int fraud)
    {
        var rings = Math.Max(1, fraud / 4 / RingSize);
        for (var ring = 1; ring <= rings; ring++)
            for (var member = 0; member < RingSize; member++)
                yield return (Persona.FraudRing, ring);

        Persona[] solo = [Persona.AtoFraudster, Persona.RegistrationFraudster, Persona.SlowBurnFraudster];
        for (var i = 0; i < fraud - rings * RingSize; i++)
            yield return (solo[i % solo.Length], 0);
    }

    // A fixed count of Travelling holders, then one of each other genuine Persona, so every Persona appears
    // even in a small Dataset.
    private static IEnumerable<(Persona Persona, int Ring)> GenuineSlots(int genuine, int travellers, Random rng)
    {
        for (var i = 0; i < travellers; i++)
            yield return (Persona.TravellingHolder, 0);

        Persona[] each = [Persona.QuietHolder, Persona.HouseMover, Persona.NewPhone, Persona.ActiveTrader];
        foreach (var persona in each)
            yield return (persona, 0);

        for (var i = travellers + each.Length; i < genuine; i++)
            yield return (rng.Next(10) switch
            {
                0 => Persona.HouseMover,
                1 => Persona.NewPhone,
                2 => Persona.ActiveTrader,
                _ => Persona.QuietHolder,
            }, 0);
    }
}
