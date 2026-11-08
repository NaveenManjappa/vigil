using Vigil.Domain.Detection;
using Vigil.Domain.Events;

namespace Vigil.Detection.Tests;

public sealed class DetectorTests
{
    private const string Account = "ACC-1";
    private static readonly DateTimeOffset Monday = new(2026, 9, 21, 9, 0, 0, TimeSpan.Zero);
    private static readonly RuleSet Rules = RuleSet.Load(Path.Combine(AppContext.BaseDirectory, "config", "rules.json"));

    private static PortalEvent Event(EventType type, DateTimeOffset at, string device = "D-1", string country = "GB") =>
        new(Guid.NewGuid(), Account, "HOLDER-1", type, at, device, "198.51.100.1", country, "Web", new Dictionary<string, string>());

    [Fact]
    public void Password_reset_then_MFA_enrolment_within_an_hour_is_a_recovery_takeover()
    {
        var reset = Event(EventType.PasswordReset, Monday);
        var mfa = Event(EventType.MfaEnrolled, Monday.AddMinutes(4));

        var hit = Assert.Single(Detector.Detect(mfa, [reset], Rules));

        Assert.Equal("R1", hit.RuleId);
        Assert.Equal(30, hit.Points);
        Assert.Equal([reset.EventId, mfa.EventId], hit.EvidenceEventIds);
        Assert.Equal(mfa.OccurredAt, hit.TriggeredAt);
        Assert.Equal("Password reset then MFA enrolment, 4 min apart; window 1h", hit.Explanation);
    }

    [Fact]
    public void MFA_enrolment_more_than_an_hour_after_the_reset_hits_nothing()
    {
        var reset = Event(EventType.PasswordReset, Monday);
        var mfa = Event(EventType.MfaEnrolled, Monday.AddMinutes(61));

        Assert.Empty(Detector.Detect(mfa, [reset], Rules));
    }

    [Fact]
    public void Only_the_event_being_detected_can_complete_a_sequence()
    {
        // The reset is the first step, not the second, so this event completes nothing.
        var mfa = Event(EventType.MfaEnrolled, Monday);
        var reset = Event(EventType.PasswordReset, Monday.AddMinutes(4));

        Assert.Empty(Detector.Detect(reset, [mfa], Rules));
    }

    [Fact]
    public void Events_that_happened_later_are_ignored_even_if_they_arrived_first()
    {
        // The reset happened after the MFA enrolment, so it cannot be its first step.
        var mfa = Event(EventType.MfaEnrolled, Monday);
        var laterReset = Event(EventType.PasswordReset, Monday.AddMinutes(4));

        Assert.Empty(Detector.Detect(mfa, [laterReset], Rules));
    }

    // One row per alternative in each step, so every listed event type is shown to count.
    [Theory]
    [InlineData("R2", EventType.PasswordReset, EventType.EmailChanged, 10, 20, "Password reset then contact change, 10 min apart; window 24h")]
    [InlineData("R2", EventType.PasswordReset, EventType.PhoneChanged, 10, 20, "Password reset then contact change, 10 min apart; window 24h")]
    [InlineData("R3", EventType.EmailChanged, EventType.BankDetailsChanged, 90, 30, "Contact change then bank-detail change, 1h 30 min apart; window 72h")]
    [InlineData("R3", EventType.PhoneChanged, EventType.BankDetailsChanged, 26 * 60, 30, "Contact change then bank-detail change, 26h apart; window 72h")]
    [InlineData("R3", EventType.AddressChanged, EventType.BankDetailsChanged, 5, 30, "Contact change then bank-detail change, 5 min apart; window 72h")]
    [InlineData("R4", EventType.BankDetailsChanged, EventType.SaleRequested, 76 * 60, 40, "Bank-detail change then sale or transfer, 76h apart; window 7d")]
    [InlineData("R4", EventType.BankDetailsChanged, EventType.TransferRequested, 48 * 60, 40, "Bank-detail change then sale or transfer, 48h apart; window 7d")]
    [InlineData("R5", EventType.AccountRegistered, EventType.BankDetailsChanged, 30, 30, "Registration then bank-detail change, 30 min apart; window 48h")]
    [InlineData("R6", EventType.AccountRegistered, EventType.SaleRequested, (10 * 24 + 6) * 60, 30, "Registration then sale or transfer, 10d 6h apart; window 14d")]
    [InlineData("R6", EventType.AccountRegistered, EventType.TransferRequested, 24 * 60, 30, "Registration then sale or transfer, 24h apart; window 14d")]
    public void A_rule_hits_when_its_second_step_follows_its_first_inside_the_window(
        string ruleId, EventType stepA, EventType stepB, int minutesApart, int points, string explanation)
    {
        var a = Event(stepA, Monday);
        var b = Event(stepB, Monday.AddMinutes(minutesApart));

        var hit = Assert.Single(Detector.Detect(b, [a], Rules));

        Assert.Equal(ruleId, hit.RuleId);
        Assert.Equal(points, hit.Points);
        Assert.Equal([a.EventId, b.EventId], hit.EvidenceEventIds);
        Assert.Equal(explanation, hit.Explanation);
    }

    [Fact]
    public void A_new_device_and_a_new_country_on_one_event_are_one_first_sighting()
    {
        var usual = Event(EventType.LoginSucceeded, Monday);
        var stranger = Event(EventType.LoginSucceeded, Monday.AddDays(1), device: "D-77", country: "RO");

        var hit = Assert.Single(Detector.Detect(stranger, [usual], Rules));

        Assert.Equal("R0", hit.RuleId);
        Assert.Equal([stranger.EventId], hit.EvidenceEventIds);
        Assert.Equal("First sighting on this Account: new device D-77, new country RO", hit.Explanation);
    }

    [Fact]
    public void An_Accounts_first_event_is_never_a_first_sighting()
    {
        var first = Event(EventType.LoginSucceeded, Monday, device: "D-77", country: "RO");

        Assert.Empty(Detector.Detect(first, [], Rules));
    }

    [Fact]
    public void A_usual_device_and_country_hit_nothing()
    {
        var usual = Event(EventType.LoginSucceeded, Monday);
        var again = Event(EventType.LoginSucceeded, Monday.AddDays(1));

        Assert.Empty(Detector.Detect(again, [usual], Rules));
    }

    [Fact]
    public void Graph_rules_are_left_to_Vigil_Graph()
    {
        // A bank change after an email change hits R3 only: G1 and G2 need the graph, which the Detector cannot see.
        var email = Event(EventType.EmailChanged, Monday);
        var bank = Event(EventType.BankDetailsChanged, Monday.AddHours(1));

        Assert.Equal(["R3"], Detector.Detect(bank, [email], Rules).Select(h => h.RuleId));
    }
}
