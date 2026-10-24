using Vigil.Domain.Datasets;
using Vigil.Domain.Detection;
using Vigil.Domain.Events;
using Vigil.Evaluation.Tuning;

namespace Vigil.Evaluation.Tests;

public sealed class WhatIfTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly RuleSet Rules = RuleSet.Load(Path.Combine(AppContext.BaseDirectory, "config", "rules.v2.json"));

    private static LabelledAccount Account(string id, Persona persona, FraudKind? kind, params (EventType Type, int Minutes, string Device)[] steps)
    {
        var events = new List<PortalEvent> { Event(id, EventType.LoginSucceeded, Start, "D-USUAL") };
        events.AddRange(steps.Select(s => Event(id, s.Type, Start.AddMinutes(s.Minutes), s.Device)));
        return new(id, new Label(persona, kind), events);
    }

    private static PortalEvent Event(string account, EventType type, DateTimeOffset at, string device) =>
        new(Guid.NewGuid(), account, $"HOLDER-{account}", type, at, device, "198.51.100.1", "GB", "web", new Dictionary<string, string>());

    // Scores worked out by hand from rules.v2.json.
    private static readonly LabelledAccount[] Dataset =
    [
        // R0 10 + R1 30 + R2 20 = 60, High at the email change, before the bank change. With R2 at 10 it is 50, Medium,
        // until R3 30 at the bank change makes it 80: still detected, but no longer early. Margaret's trade-off.
        Account("MARGARET", Persona.AtoFraudster, FraudKind.Ato,
            (EventType.PasswordReset, 1_000, "D-X"), (EventType.MfaEnrolled, 1_005, "D-X"),
            (EventType.EmailChanged, 1_010, "D-X"), (EventType.BankDetailsChanged, 2_000, "D-X")),

        // R0 10 + R1 30 + R2 20 = 60, a false positive. With R2 at 10 it is 50, Medium, and no longer flagged.
        Account("TRAVELLER", Persona.TravellingHolder, null,
            (EventType.PasswordReset, 1_000, "D-ABROAD"), (EventType.MfaEnrolled, 1_005, "D-ABROAD"),
            (EventType.EmailChanged, 1_010, "D-ABROAD")),
    ];

    [Fact]
    public void Halving_R2_quiets_the_traveller_but_costs_the_early_detection_of_Margarets_takeover()
    {
        var whatIf = WhatIf.Measure(Dataset, Rules, new WeightSuggestion("R2", "Contact hijack", new Rate(1, 5), 20, 10));

        Assert.Equal(new Rate(1, 1), whatIf.Before.DetectionRate);
        Assert.Equal(new Rate(1, 1), whatIf.Before.FalsePositiveRate);
        Assert.Equal(new Rate(1, 1), whatIf.Before.EarlyDetectionRate);

        Assert.Equal(new Rate(1, 1), whatIf.After.DetectionRate);
        Assert.Equal(new Rate(0, 1), whatIf.After.FalsePositiveRate);
        Assert.Equal(new Rate(0, 1), whatIf.After.EarlyDetectionRate);
    }

    [Fact]
    public void A_Graph_Rules_suggestion_re_weights_the_graph_hits_passed_in()
    {
        // R0 10 + R1 30 + G1 30 = 70, High, a false positive. With G1 at 15 it is 55, Medium.
        var newPhone = Account("NEW-PHONE", Persona.NewPhone, null,
            (EventType.PasswordReset, 1_000, "D-NEW"), (EventType.MfaEnrolled, 1_005, "D-NEW"));
        var graphHits = new Dictionary<string, IReadOnlyList<RuleHit>>
        {
            [newPhone.AccountId] = [new RuleHit("G1", 30, [newPhone.Events[^1].EventId], Start.AddMinutes(1_010), "shared bank account")],
        };

        var whatIf = WhatIf.Measure([newPhone], Rules, new WeightSuggestion("G1", "Shared bank account", new Rate(0, 5), 30, 15), graphHits);

        Assert.Equal(new Rate(1, 1), whatIf.Before.FalsePositiveRate);
        Assert.Equal(new Rate(0, 1), whatIf.After.FalsePositiveRate);
    }
}
