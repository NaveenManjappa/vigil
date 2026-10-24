using Vigil.Domain.Datasets;
using Vigil.Domain.Detection;
using Vigil.Domain.Events;
using Vigil.Simulator.Personas;

namespace Vigil.Evaluation.Tests;

public sealed class EvaluatorTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly RuleSet Rules = RuleSet.Load(Path.Combine(AppContext.BaseDirectory, "config", "rules.v1.json"));

    /// <summary>
    /// A login from the usual device, then each step a number of minutes after it.
    /// A step with a device other than "D-USUAL" is a First Sighting.
    /// </summary>
    private static LabelledAccount Account(string id, Persona persona, FraudKind? kind, params (EventType Type, int Minutes, string Device)[] steps)
    {
        var events = new List<PortalEvent> { Event(id, EventType.LoginSucceeded, Start, "D-USUAL") };
        events.AddRange(steps.Select(s => Event(id, s.Type, Start.AddMinutes(s.Minutes), s.Device)));
        return new(id, new Label(persona, kind), events);
    }

    private static PortalEvent Event(string account, EventType type, DateTimeOffset at, string device) =>
        new(Guid.NewGuid(), account, $"HOLDER-{account}", type, at, device, "198.51.100.1", "GB", "web", new Dictionary<string, string>());

    // Each Account's Score is worked out by hand from rules.v1.json in its comment.
    private static readonly LabelledAccount[] HandLabelled =
    [
        // R0 10 + R1 30 + R2 20 = 60, High at the email change; R3 at the bank change makes it 90. Flagged early.
        Account("ATO-EARLY", Persona.AtoFraudster, FraudKind.Ato,
            (EventType.PasswordReset, 1_000, "D-X"), (EventType.MfaEnrolled, 1_005, "D-X"),
            (EventType.EmailChanged, 1_010, "D-X"), (EventType.BankDetailsChanged, 2_000, "D-X")),

        // R3 30 at the bank change, then R4 40 = 70, High only at the sale. Flagged, but after the bank change.
        Account("ATO-LATE", Persona.AtoFraudster, FraudKind.Ato,
            (EventType.EmailChanged, 1_000, "D-USUAL"), (EventType.BankDetailsChanged, 2_000, "D-USUAL"),
            (EventType.SaleRequested, 3_000, "D-USUAL")),

        // R0 10 only: every later step is outside its Window. Missed.
        Account("SLOW-BURN", Persona.SlowBurnFraudster, FraudKind.Evasion,
            (EventType.PasswordReset, 1_000, "D-X"), (EventType.MfaEnrolled, 1_200, "D-X")),

        // R0 10 + R1 30 = 40, Medium. Not flagged.
        Account("NEW-PHONE", Persona.NewPhone, null,
            (EventType.PasswordReset, 1_000, "D-NEW"), (EventType.MfaEnrolled, 1_005, "D-NEW")),

        // R0 10 + R1 30 + R2 20 = 60, High. A false positive.
        Account("NEW-PHONE-AND-EMAIL", Persona.NewPhone, null,
            (EventType.PasswordReset, 1_000, "D-NEW"), (EventType.MfaEnrolled, 1_005, "D-NEW"),
            (EventType.EmailChanged, 1_010, "D-NEW")),

        // No hits.
        Account("QUIET", Persona.QuietHolder, null, (EventType.LoginSucceeded, 1_000, "D-USUAL")),
    ];

    [Fact]
    public void A_hand_labelled_Dataset_gives_the_metrics_worked_out_by_hand()
    {
        var result = Evaluator.Evaluate(HandLabelled, Rules);

        Assert.Equal(new Rate(2, 3), result.DetectionRate);
        Assert.Equal(new Rate(1, 3), result.FalsePositiveRate);
        Assert.Equal(new Rate(1, 2), result.EarlyDetectionRate);

        Assert.Equal(
            [
                ("R0", new Rate(2, 4)),
                ("R1", new Rate(1, 3)),
                ("R2", new Rate(1, 2)),
                ("R3", new Rate(2, 2)),
                ("R4", new Rate(1, 1)),
                ("R5", new Rate(0, 0)),
                ("R6", new Rate(0, 0)),
            ],
            result.RulePrecision.Select(p => (p.RuleId, p.Precision)));
    }

    [Fact]
    public void On_the_seed_42_Dataset_slow_burn_is_a_known_miss_and_the_ring_peaks_at_Medium()
    {
        var result = Evaluator.Evaluate(DatasetGenerator.Generate(seed: 42, holders: 1000), Rules);

        var slowBurn = result.Personas.Single(p => p.Persona == Persona.SlowBurnFraudster);
        Assert.True(slowBurn.Accounts > 0);
        Assert.Equal(0, slowBurn.Flagged);

        // Each ring Account alone is only moderately risky until Stage 2's graph rules link them.
        var ring = result.Personas.Single(p => p.Persona == Persona.FraudRing);
        Assert.Equal(ring.Accounts, ring.PeakBands[RiskBand.Medium]);

        var ato = result.Personas.Single(p => p.Persona == Persona.AtoFraudster);
        Assert.Equal(ato.Accounts, ato.Flagged);
    }

    [Fact]
    public void On_the_seed_42_Dataset_every_Travelling_holder_peaks_at_High_so_FPR_is_not_zero()
    {
        var result = Evaluator.Evaluate(DatasetGenerator.Generate(seed: 42, holders: 1000), Rules);

        // R0 10 + R1 30 + R2 20 = 60: exactly the High threshold, short of Critical.
        var travellers = result.Personas.Single(p => p.Persona == Persona.TravellingHolder);
        Assert.True(travellers.Accounts > 0);
        Assert.Equal(travellers.Accounts, travellers.PeakBands[RiskBand.High]);
        Assert.Equal(travellers.Accounts, result.FalsePositiveRate.Count);
    }
}
