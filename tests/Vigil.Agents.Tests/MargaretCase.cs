using Vigil.Domain.Detection;
using Vigil.Domain.Events;
using Vigil.Domain.Graph;

namespace Vigil.Agents.Tests;

/// <summary>
/// Margaret's takeover (PROJECT.md Section 3.1) as an in-memory Investigation source: laptop logins from GB, then a
/// fraudster on D-77 from RO, a device Confirmed Fraud Account ACC-PRIYA also used.
/// </summary>
public sealed class MargaretCase : IInvestigationSource
{
    private static readonly DateTimeOffset Start = new(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);

    public static readonly PortalEvent Login = Event(Start, EventType.LoginSucceeded, "D-MARGARET-LAPTOP", "203.0.113.51", "GB");
    public static readonly PortalEvent Reset = Event(Start.AddDays(20), EventType.PasswordReset, "D-77", "198.51.100.77", "RO");
    public static readonly PortalEvent Mfa = Event(Reset.OccurredAt.AddMinutes(4), EventType.MfaEnrolled, "D-77", "198.51.100.77", "RO");
    public static readonly PortalEvent Email = Event(Reset.OccurredAt.AddMinutes(10), EventType.EmailChanged, "D-77", "198.51.100.77", "RO",
        // OldEmail and Name are attributes the masker has no rule for, as a new portal field would arrive.
        new() { ["NewEmail"] = "m.attacker@example.net", ["OldEmail"] = "margaret.hale@example.com", ["Name"] = "Margaret Hale" });
    public static readonly PortalEvent Bank = Event(Reset.OccurredAt.AddDays(1), EventType.BankDetailsChanged, "D-77", "198.51.100.77", "RO",
        new() { ["NewBankAccountHash"] = "BA-9f31", ["Amount"] = "25000.00" });

    public static readonly PortalEvent PriyaReset = new(Guid.NewGuid(), "ACC-PRIYA", "HOLDER-PRIYA", EventType.PasswordReset,
        Start.AddDays(5), "D-77", "198.51.100.78", "RO", "web", new Dictionary<string, string>());
    public static readonly Guid DerekReset = Guid.NewGuid();

    public static readonly Alert Alert = new("ACC-MARGARET", 105, RiskBand.Critical, "v2",
    [
        new RuleHit("R0", 10, [Reset.EventId], Reset.OccurredAt, "First sighting on this Account: new device D-77, new country RO"),
        new RuleHit("G2", 25, [PriyaReset.EventId, Reset.EventId], Reset.OccurredAt, "Shares Device D-77 with Confirmed Fraud Account ACC-PRIYA"),
        new RuleHit("R1", 30, [Reset.EventId, Mfa.EventId], Mfa.OccurredAt, "Password reset then MFA enrolment, 4m apart; window 24h"),
        new RuleHit("R2", 20, [Reset.EventId, Email.EventId], Email.OccurredAt, "Password reset then contact change, 10m apart; window 72h"),
    ]);

    /// <summary>Every identifier value in this Case, none of which may ever reach the model.</summary>
    public static readonly string[] RawIdentifiers =
    [
        "ACC-MARGARET", "HOLDER-MARGARET", "D-MARGARET-LAPTOP", "203.0.113.51", "D-77", "198.51.100.77",
        "m.attacker@example.net", "margaret.hale@example.com", "Margaret Hale", "BA-9f31", "ACC-PRIYA", "HOLDER-PRIYA", "198.51.100.78", "ACC-DEREK",
    ];

    private static readonly PortalEvent[] Timeline = [Login, Reset, Mfa, Email, Bank];

    public List<int> TimelineDaysAsked { get; } = [];

    public Task<IReadOnlyList<PortalEvent>> GetEventsAsync(IReadOnlyCollection<Guid> eventIds, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<PortalEvent>>(
            [.. Timeline.Append(PriyaReset).Where(e => eventIds.Contains(e.EventId)).OrderBy(e => e.OccurredAt)]);

    public Task<IReadOnlyList<PortalEvent>> GetTimelineAsync(string accountId, int days, CancellationToken ct)
    {
        TimelineDaysAsked.Add(days);
        var last = Timeline[^1].OccurredAt;
        return Task.FromResult<IReadOnlyList<PortalEvent>>([.. Timeline.Where(e => e.AccountId == accountId && e.OccurredAt >= last.AddDays(-days))]);
    }

    public Task<Neighbourhood> GetNeighbourhoodAsync(string accountId, CancellationToken ct) =>
        Task.FromResult(new Neighbourhood(accountId,
        [
            new LinkedIdentifier("BankAccount", "BA-9f31", Bank.EventId, []),
            new LinkedIdentifier("Device", "D-77", Reset.EventId,
                [new LinkedAccount("ACC-DEREK", false, DerekReset), new LinkedAccount("ACC-PRIYA", true, PriyaReset.EventId)]),
            new LinkedIdentifier("Device", "D-MARGARET-LAPTOP", Login.EventId, []),
            new LinkedIdentifier("Email", "m.attacker@example.net", Email.EventId, []),
        ]));

    public Task<HolderBaseline> GetHolderBaselineAsync(string accountId, DateTimeOffset before, CancellationToken ct) =>
        Task.FromResult(new HolderBaseline(["GB"], ["D-MARGARET-LAPTOP"], LoginsPerWeek: 3.5));

    private static PortalEvent Event(
        DateTimeOffset at, EventType type, string device, string ip, string country, Dictionary<string, string>? attributes = null) =>
        new(Guid.NewGuid(), "ACC-MARGARET", "HOLDER-MARGARET", type, at, device, ip, country, "web", attributes ?? []);
}
