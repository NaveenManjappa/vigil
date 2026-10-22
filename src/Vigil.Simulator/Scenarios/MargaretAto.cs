using Vigil.Domain.Events;

namespace Vigil.Simulator.Scenarios;

/// <summary>
/// PROJECT.md Section 3.1: a fraudster with Margaret's email password takes over her account
/// from a new device in Romania, then tries to redirect payouts the next day.
/// </summary>
public sealed class MargaretAto : Scenario
{
    private const string Account = "ACC-MARGARET";
    private const string Holder = "HOLDER-MARGARET";
    private const string FraudDevice = "D-77";
    private const string RomanianIp = "198.51.100.77"; // documentation range, not a real address

    public override string Name => "margaret-ato";

    public override IReadOnlyList<PortalEvent> Events() =>
    [
        Fraudster(1, EventType.PasswordReset, Anchor),
        Fraudster(2, EventType.MfaEnrolled, Anchor.AddMinutes(4)),
        Fraudster(3, EventType.EmailChanged, Anchor.AddMinutes(10), new() { ["NewEmail"] = "m.attacker@example.net" }),
        Fraudster(4, EventType.BankDetailsChanged, Anchor.AddDays(1).AddHours(5), new() { ["NewBankAccountHash"] = "BA-9f31" }),
    ];

    private PortalEvent Fraudster(int step, EventType type, DateTimeOffset at, Dictionary<string, string>? attributes = null) =>
        new(IdFor(step), Account, Holder, type, at, FraudDevice, RomanianIp, "RO", "web", attributes ?? []);
}
