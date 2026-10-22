using Vigil.Domain.Datasets;
using Vigil.Domain.Events;
using Vigil.Simulator.Personas;

namespace Vigil.Simulator.Tests;

public sealed class DatasetGeneratorTests
{
    private static readonly IReadOnlyList<LabelledAccount> Dataset42 = DatasetGenerator.Generate(seed: 42, holders: 1000);

    private static byte[] Bytes(int seed, int holders)
    {
        using var stream = new MemoryStream();
        Dataset.Write(DatasetGenerator.Generate(seed, holders), stream);
        return stream.ToArray();
    }

    [Fact]
    public void The_same_seed_and_holder_count_give_a_byte_identical_Dataset()
    {
        Assert.Equal(Bytes(seed: 42, holders: 1000), Bytes(seed: 42, holders: 1000));
    }

    [Fact]
    public void A_Dataset_read_back_from_its_file_writes_the_same_bytes()
    {
        var written = Bytes(seed: 42, holders: 1000);

        using var reread = new MemoryStream();
        Dataset.Write(Dataset.Read(new MemoryStream(written)), reread);

        Assert.Equal(written, reread.ToArray());
    }

    [Fact]
    public void A_different_seed_gives_a_different_Dataset()
    {
        Assert.NotEqual(Bytes(seed: 42, holders: 1000), Bytes(seed: 43, holders: 1000));
    }

    [Fact]
    public void About_three_percent_of_Accounts_are_fraud_and_every_Persona_appears()
    {
        Assert.Equal(1000, Dataset42.Select(a => a.AccountId).Distinct().Count());
        Assert.InRange(Dataset42.Count(a => a.Label.IsFraud), 25, 35);
        Assert.Equal(Enum.GetValues<Persona>(), Dataset42.Select(a => a.Label.Persona).Distinct().Order());
        Assert.Equal(Enum.GetValues<FraudKind>(), Dataset42.Select(a => a.Label.FraudKind).OfType<FraudKind>().Distinct().Order());
    }

    [Fact]
    public void Registration_fraud_starts_with_AccountRegistered()
    {
        var registrations = Dataset42.Where(a => a.Label.Persona == Persona.RegistrationFraudster);

        Assert.All(registrations, a => Assert.Equal(EventType.AccountRegistered, a.Events[0].Type));
    }

    [Fact]
    public void Every_other_Account_starts_with_baseline_logins_from_one_usual_device_and_country()
    {
        var withBaseline = Dataset42.Where(a => a.Label.Persona != Persona.RegistrationFraudster);

        Assert.All(withBaseline, a =>
        {
            var first = a.Events[0];
            Assert.Equal(EventType.LoginSucceeded, first.Type);
            Assert.True(first.OccurredAt - DatasetGenerator.ClockStart <= TimeSpan.FromDays(10), $"{a.AccountId} baseline starts late");

            // Trading is the active trader's everyday behaviour, so only the other Personas have a journey to precede.
            var journey = a.Events.FirstOrDefault(e => e.Type is not EventType.LoginSucceeded || e.DeviceId != first.DeviceId);
            if (journey is not null && a.Label.Persona != Persona.ActiveTrader)
                Assert.True(journey.OccurredAt - first.OccurredAt >= TimeSpan.FromDays(60), $"{a.AccountId} baseline is short");

            Assert.All(a.Events.TakeWhile(e => e != journey), e => Assert.Equal((first.DeviceId, first.Country), (e.DeviceId, e.Country)));
        });
    }

    // R1–R4 of PROJECT.md Section 7.2 (step A, step B, Window): the takeover steps the slow-burn journey walks
    // through. Checked against the spec rather than config, so retuning a Window cannot quietly redefine the Persona.
    public static TheoryData<EventType[], EventType[], int> SectionSevenTwoRules => new()
    {
        { [EventType.PasswordReset], [EventType.MfaEnrolled], 60 },
        { [EventType.PasswordReset], [EventType.EmailChanged, EventType.PhoneChanged], 24 * 60 },
        { [EventType.EmailChanged, EventType.PhoneChanged, EventType.AddressChanged], [EventType.BankDetailsChanged], 72 * 60 },
        { [EventType.BankDetailsChanged], [EventType.SaleRequested, EventType.TransferRequested], 7 * 24 * 60 },
    };

    [Theory]
    [MemberData(nameof(SectionSevenTwoRules))]
    public void The_slow_burn_fraudster_spaces_each_step_beyond_the_rule_Window(EventType[] stepA, EventType[] stepB, int windowMinutes)
    {
        var slowBurns = Dataset42.Where(a => a.Label.Persona == Persona.SlowBurnFraudster).ToList();
        Assert.NotEmpty(slowBurns);

        foreach (var account in slowBurns)
        {
            var completions = account.Events.Where(e => stepB.Contains(e.Type)).ToList();
            Assert.NotEmpty(completions);
            foreach (var b in completions)
            {
                // As Detection pairs them: step A is the latest earlier event of an A type.
                var a = account.Events.LastOrDefault(e => stepA.Contains(e.Type) && e.OccurredAt <= b.OccurredAt && e != b);
                Assert.NotNull(a);
                Assert.True(b.OccurredAt - a.OccurredAt > TimeSpan.FromMinutes(windowMinutes),
                    $"{account.AccountId}: {a.Type} -> {b.Type} is {b.OccurredAt - a.OccurredAt}, inside the {windowMinutes} min Window");
            }
        }
    }

    [Fact]
    public void Each_fraud_ring_is_five_Accounts_sharing_a_device_an_IP_and_a_bank_account()
    {
        var rings = Dataset42.Where(a => a.Label.Persona == Persona.FraudRing)
            .GroupBy(a => a.Events.Single(e => e.Type == EventType.BankDetailsChanged).Attributes["NewBankAccountHash"]);

        Assert.All(rings, ring =>
        {
            Assert.Equal(DatasetGenerator.RingSize, ring.Count());
            var fraudEvents = ring.Select(a => a.Events.Where(e => e.Type is EventType.EmailChanged or EventType.BankDetailsChanged));
            Assert.Single(fraudEvents.SelectMany(es => es).Select(e => (e.DeviceId, e.IpAddress)).Distinct());
        });
    }

    [Fact]
    public void Every_Account_is_inside_the_simulated_clock_and_in_timeline_order()
    {
        var clockEnd = DatasetGenerator.ClockStart + DatasetGenerator.ClockSpan;

        Assert.All(Dataset42, a =>
        {
            Assert.All(a.Events, e => Assert.InRange(e.OccurredAt, DatasetGenerator.ClockStart, clockEnd));
            Assert.Equal(a.Events.OrderBy(e => e.OccurredAt).ThenBy(e => e.EventId), a.Events);
        });
    }
}
