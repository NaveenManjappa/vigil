using Vigil.Domain.Datasets;
using Vigil.Domain.Events;

namespace Vigil.Simulator.Personas;

internal sealed class AccountBuilder
{
    private static readonly string[] FraudCountries = ["RO", "NG", "BR", "VN"];
    private static readonly string[] TravelCountries = ["ES", "FR", "US", "TH"];

    private readonly string datasetName;
    private readonly int number;
    private readonly Random rng;
    private readonly List<PortalEvent> events = [];

    public AccountBuilder(string datasetName, int number, Random rng)
    {
        this.datasetName = datasetName;
        this.number = number;
        this.rng = rng;
        AccountId = $"ACC-{number:D5}";
        HolderId = $"HOLDER-{number:D5}";
    }

    public string AccountId { get; }
    public string HolderId { get; }

    private string UsualDevice => $"D-{number:D5}";

    // IPv6 documentation prefix: unique per Account, so unrelated Accounts never appear to share an IP.
    private string UsualIp => $"2001:db8::{number:x}";

    private static DateTimeOffset ClockEnd => DatasetGenerator.ClockStart + DatasetGenerator.ClockSpan;

    /// <param name="ring">Which fraud ring the Account belongs to; ignored for other Personas.</param>
    public LabelledAccount Build(Persona persona, int ring)
    {
        FraudKind? kind = persona switch
        {
            Persona.QuietHolder => QuietHolder(),
            Persona.HouseMover => HouseMover(),
            Persona.NewPhone => NewPhone(),
            Persona.ActiveTrader => ActiveTrader(),
            Persona.TravellingHolder => TravellingHolder(),
            Persona.AtoFraudster => AtoFraudster(),
            Persona.RegistrationFraudster => RegistrationFraudster(),
            Persona.FraudRing => FraudRing(ring),
            Persona.SlowBurnFraudster => SlowBurnFraudster(),
            _ => throw new ArgumentOutOfRangeException(nameof(persona), persona, null),
        };

        return new(AccountId, new Label(persona, kind),
            [.. events.OrderBy(e => e.OccurredAt).ThenBy(e => e.EventId)]);
    }

    private FraudKind? QuietHolder()
    {
        Baseline(until: ClockEnd, TimeSpan.FromDays(4), TimeSpan.FromDays(10));
        return null;
    }

    private FraudKind? HouseMover()
    {
        var start = BaselineThenJourney();
        AsHolder(EventType.AddressChanged, start, new() { ["NewPostcode"] = $"RG{rng.Next(1, 10)} {rng.Next(1, 10)}AA" });
        AsHolder(EventType.PhoneChanged, After(start, TimeSpan.FromDays(1), TimeSpan.FromDays(3)), new() { ["NewPhone"] = Phone() });
        return null;
    }

    /// <summary>A genuine recovery from a new handset: the realistic false-positive pressure on the recovery rules.</summary>
    private FraudKind? NewPhone()
    {
        var newPhone = $"D-{number:D5}-NEW";
        var requested = BaselineThenJourney();
        Add(EventType.PasswordResetRequested, requested, newPhone, UsualIp, "GB");
        var reset = After(requested, TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(10));
        Add(EventType.PasswordReset, reset, newPhone, UsualIp, "GB");
        Add(EventType.MfaEnrolled, After(reset, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(20)), newPhone, UsualIp, "GB");
        return null;
    }

    private FraudKind? ActiveTrader()
    {
        foreach (var login in Baseline(until: ClockEnd, TimeSpan.FromHours(12), TimeSpan.FromDays(2)))
            if (rng.Next(4) == 0)
            {
                // A login just before the clock ends leaves no room for its sale, and every event must stay on the clock.
                var sale = After(login, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30));
                if (sale <= ClockEnd) AsHolder(EventType.SaleRequested, sale, Sale());
            }
        return null;
    }

    /// <summary>
    /// A genuine Holder abroad on a new device who resets the password, enrols MFA and changes phone, each step
    /// inside the R1 or R2 Window. It reaches High (R0 + R1 + R2 = 60): the false positive the feedback loop learns from.
    /// </summary>
    private FraudKind? TravellingHolder()
    {
        var device = $"D-{number:D5}-TRAVEL";
        var ip = $"2001:db8:a::{number:x}";
        var country = TravelCountries[rng.Next(TravelCountries.Length)];
        var reset = BaselineThenJourney();
        Add(EventType.PasswordReset, reset, device, ip, country);
        Add(EventType.MfaEnrolled, After(reset, TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(45)), device, ip, country);
        Add(EventType.PhoneChanged, After(reset, TimeSpan.FromHours(1), TimeSpan.FromHours(20)), device, ip, country,
            new() { ["NewPhone"] = Phone() });
        return null;
    }

    /// <summary>Section 3.1's takeover: reset, MFA, email, bank and sale from a new device abroad, each step well inside its Window.</summary>
    private FraudKind? AtoFraudster()
    {
        var reset = BaselineThenJourney();
        var mfa = After(reset, TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(15));
        var email = After(mfa, TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(20));
        var bank = After(email, TimeSpan.FromHours(20), TimeSpan.FromDays(2));
        var sale = After(bank, TimeSpan.FromDays(1), TimeSpan.FromDays(3));
        Takeover(reset, mfa, email, bank, sale);
        return FraudKind.Ato;
    }

    /// <summary>
    /// The same steps as the ATO fraudster, each just beyond the Window of the Section 7.2 rule that links it
    /// to the step before (R1 1h, R2 24h, R3 72h, R4 7d), so windowed rules miss it.
    /// </summary>
    private FraudKind? SlowBurnFraudster()
    {
        var reset = BaselineThenJourney();
        var mfa = After(reset, TimeSpan.FromHours(2), TimeSpan.FromHours(6));
        var email = After(reset, TimeSpan.FromDays(2), TimeSpan.FromDays(3));
        var bank = After(email, TimeSpan.FromDays(3) + TimeSpan.FromHours(1), TimeSpan.FromDays(4));
        var sale = After(bank, TimeSpan.FromDays(7) + TimeSpan.FromHours(1), TimeSpan.FromDays(8));
        Takeover(reset, mfa, email, bank, sale);
        return FraudKind.Evasion;
    }

    /// <summary>A brand-new Account opened with a stolen identity, so it has no baseline to compare against.</summary>
    private FraudKind? RegistrationFraudster()
    {
        var (device, ip, country) = Fraudster();
        var registered = JourneyStart();
        var bank = After(registered, TimeSpan.FromHours(2), TimeSpan.FromHours(30));
        Add(EventType.AccountRegistered, registered, device, ip, country);
        Add(EventType.BankDetailsChanged, bank, device, ip, country, new() { ["NewBankAccountHash"] = BankAccount() });
        Add(EventType.SaleRequested, After(bank, TimeSpan.FromDays(1), TimeSpan.FromDays(4)), device, ip, country, Sale());
        return FraudKind.Registration;
    }

    /// <summary>
    /// One of five Accounts taken over by the same ring, which reuses its device, IP and payout bank account.
    /// Each Account alone looks only moderately risky; the shared identifiers are what Stage 2's graph rules find.
    /// </summary>
    private FraudKind? FraudRing(int ring)
    {
        var device = $"D-RING-{ring}";
        var ip = $"192.0.2.{ring}"; // documentation range, not a real address
        var login = BaselineThenJourney();
        var email = After(login, TimeSpan.FromMinutes(5), TimeSpan.FromHours(1));
        Add(EventType.LoginSucceeded, login, device, ip, "GB");
        Add(EventType.EmailChanged, email, device, ip, "GB", new() { ["NewEmail"] = $"ring{ring}.{number}@example.net" });
        Add(EventType.BankDetailsChanged, After(email, TimeSpan.FromDays(1), TimeSpan.FromDays(2)), device, ip, "GB",
            new() { ["NewBankAccountHash"] = $"BA-RING-{ring}" });
        return FraudKind.Ring;
    }

    private void Takeover(DateTimeOffset reset, DateTimeOffset mfa, DateTimeOffset email, DateTimeOffset bank, DateTimeOffset sale)
    {
        var (device, ip, country) = Fraudster();
        Add(EventType.PasswordReset, reset, device, ip, country);
        Add(EventType.MfaEnrolled, mfa, device, ip, country);
        Add(EventType.EmailChanged, email, device, ip, country, new() { ["NewEmail"] = $"takeover.{number}@example.net" });
        Add(EventType.BankDetailsChanged, bank, device, ip, country, new() { ["NewBankAccountHash"] = BankAccount() });
        Add(EventType.SaleRequested, sale, device, ip, country, Sale());
    }

    private (string Device, string Ip, string Country) Fraudster() =>
        ($"D-X{number:D5}", $"2001:db8:f::{number:x}", FraudCountries[rng.Next(FraudCountries.Length)]);

    /// <summary>Baseline logins up to a journey start late in the clock, which leaves room for the slowest journey to finish.</summary>
    private DateTimeOffset BaselineThenJourney()
    {
        var start = JourneyStart();
        Baseline(until: start, TimeSpan.FromDays(2), TimeSpan.FromDays(7));
        return start;
    }

    private DateTimeOffset JourneyStart() => After(DatasetGenerator.ClockStart, TimeSpan.FromDays(68), TimeSpan.FromDays(74));

    /// <summary>The usual device and country that first-sighting rules compare later events against.</summary>
    private List<DateTimeOffset> Baseline(DateTimeOffset until, TimeSpan minGap, TimeSpan maxGap)
    {
        var logins = new List<DateTimeOffset>();
        for (var at = After(DatasetGenerator.ClockStart, minGap, maxGap); at < until; at = After(at, minGap, maxGap))
        {
            AsHolder(EventType.LoginSucceeded, at);
            logins.Add(at);
        }
        return logins;
    }

    // Whole minutes keep the JSON readable and the timestamps free of float rounding.
    private DateTimeOffset After(DateTimeOffset at, TimeSpan min, TimeSpan max) =>
        at.AddMinutes(rng.Next((int)min.TotalMinutes, (int)max.TotalMinutes + 1));

    private string Phone() => $"+44 7700 9{rng.Next(100000):D5}";
    private string BankAccount() => $"BA-{rng.Next(0x10000):x4}";
    private Dictionary<string, string> Sale() => new() { ["Amount"] = $"{rng.Next(5, 500) * 100}" };

    private void AsHolder(EventType type, DateTimeOffset at, Dictionary<string, string>? attributes = null) =>
        Add(type, at, UsualDevice, UsualIp, "GB", attributes);

    private void Add(EventType type, DateTimeOffset at, string device, string ip, string country, Dictionary<string, string>? attributes = null) =>
        events.Add(new(
            DeterministicGuid.Create($"{datasetName}/{AccountId}/{events.Count + 1}"),
            AccountId, HolderId, type, at, device, ip, country, "web", attributes ?? []));
}
