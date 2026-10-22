using Vigil.Domain.Detection;
using Vigil.Domain.Events;

namespace Vigil.Detection.Tests;

public sealed class DetectorTests
{
    private const string Account = "ACC-1";
    private static readonly DateTimeOffset Monday = new(2026, 9, 21, 9, 0, 0, TimeSpan.Zero);
    private static readonly RuleSet Rules = RuleSet.Load(Path.Combine(AppContext.BaseDirectory, "config", "rules.v1.json"));

    private static PortalEvent Event(EventType type, DateTimeOffset at) =>
        new(Guid.NewGuid(), Account, "HOLDER-1", type, at, "D-1", "198.51.100.1", "GB", "web", new Dictionary<string, string>());

    [Fact]
    public void Password_reset_then_MFA_enrolment_within_an_hour_is_a_recovery_takeover()
    {
        var reset = Event(EventType.PasswordReset, Monday);
        var mfa = Event(EventType.MfaEnrolled, Monday.AddMinutes(4));

        var alert = Assert.Single(Detector.Detect([reset, mfa], Rules));

        Assert.Equal(Account, alert.AccountId);
        Assert.Equal(30, alert.Score);
        Assert.Equal(RiskBand.Medium, alert.Band);
        Assert.Equal("v1", alert.RulesVersion);

        var hit = Assert.Single(alert.Hits);
        Assert.Equal("R1", hit.RuleId);
        Assert.Equal(30, hit.Points);
        Assert.Equal([reset.EventId, mfa.EventId], hit.EvidenceEventIds);
        Assert.Equal(mfa.OccurredAt, hit.TriggeredAt);
        Assert.Equal("Password reset then MFA enrolment, 4 min apart; window 1h", hit.Explanation);
    }

    [Fact]
    public void MFA_enrolment_more_than_an_hour_after_the_reset_raises_no_alert()
    {
        var timeline = new[] { Event(EventType.PasswordReset, Monday), Event(EventType.MfaEnrolled, Monday.AddMinutes(61)) };

        Assert.Empty(Detector.Detect(timeline, Rules));
    }

    [Fact]
    public void MFA_enrolment_before_the_reset_is_not_a_recovery_takeover()
    {
        var timeline = new[] { Event(EventType.MfaEnrolled, Monday), Event(EventType.PasswordReset, Monday.AddMinutes(4)) };

        Assert.Empty(Detector.Detect(timeline, Rules));
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

        var hit = Assert.Single(Assert.Single(Detector.Detect([a, b], Rules)).Hits);

        Assert.Equal(ruleId, hit.RuleId);
        Assert.Equal(points, hit.Points);
        Assert.Equal([a.EventId, b.EventId], hit.EvidenceEventIds);
        Assert.Equal(explanation, hit.Explanation);
    }

    public static TheoryData<string, EventType, EventType, int> EveryRule => new()
    {
        { "R1", EventType.PasswordReset, EventType.MfaEnrolled, 60 },
        { "R2", EventType.PasswordReset, EventType.PhoneChanged, 24 * 60 },
        { "R3", EventType.AddressChanged, EventType.BankDetailsChanged, 72 * 60 },
        { "R4", EventType.BankDetailsChanged, EventType.TransferRequested, 7 * 24 * 60 },
        { "R5", EventType.AccountRegistered, EventType.BankDetailsChanged, 48 * 60 },
        { "R6", EventType.AccountRegistered, EventType.SaleRequested, 14 * 24 * 60 },
    };

    [Theory]
    [MemberData(nameof(EveryRule))]
    public void A_second_step_exactly_on_the_window_boundary_hits(string ruleId, EventType stepA, EventType stepB, int windowMinutes)
    {
        var timeline = new[] { Event(stepA, Monday), Event(stepB, Monday.AddMinutes(windowMinutes)) };

        var hit = Assert.Single(Assert.Single(Detector.Detect(timeline, Rules)).Hits);
        Assert.Equal(ruleId, hit.RuleId);
    }

    [Theory]
    [MemberData(nameof(EveryRule))]
    public void A_second_step_one_second_past_the_window_does_not_hit(string _, EventType stepA, EventType stepB, int windowMinutes)
    {
        var timeline = new[] { Event(stepA, Monday), Event(stepB, Monday.AddMinutes(windowMinutes).AddSeconds(1)) };

        Assert.Empty(Detector.Detect(timeline, Rules));
    }

    [Theory]
    [MemberData(nameof(EveryRule))]
    public void Steps_in_the_wrong_order_do_not_hit(string _, EventType stepA, EventType stepB, int windowMinutes)
    {
        var timeline = new[] { Event(stepB, Monday), Event(stepA, Monday.AddMinutes(windowMinutes / 2)) };

        Assert.Empty(Detector.Detect(timeline, Rules));
    }

    [Fact]
    public void Step_A_is_the_latest_matching_event_before_step_B()
    {
        var oldReset = Event(EventType.PasswordReset, Monday);
        var reset = Event(EventType.PasswordReset, Monday.AddHours(3));
        var mfa = Event(EventType.MfaEnrolled, Monday.AddHours(3).AddMinutes(20));

        var hit = Assert.Single(Assert.Single(Detector.Detect([oldReset, reset, mfa], Rules)).Hits);

        Assert.Equal([reset.EventId, mfa.EventId], hit.EvidenceEventIds);
    }

    [Fact]
    public void Events_are_paired_by_when_they_happened_not_by_arrival_order()
    {
        var reset = Event(EventType.PasswordReset, Monday);
        var mfa = Event(EventType.MfaEnrolled, Monday.AddMinutes(4));

        // The MFA enrolment reached Vigil first; the source delayed the reset.
        var hit = Assert.Single(Assert.Single(Detector.Detect([mfa, reset], Rules)).Hits);

        Assert.Equal("R1", hit.RuleId);
        Assert.Equal([reset.EventId, mfa.EventId], hit.EvidenceEventIds);
    }

    [Fact]
    public void Events_at_the_same_instant_give_the_same_alert_whatever_order_they_arrive_in()
    {
        var reset = Event(EventType.PasswordReset, Monday);
        var mfa = Event(EventType.MfaEnrolled, Monday);

        var forwards = Detector.Detect([reset, mfa], Rules);
        var backwards = Detector.Detect([mfa, reset], Rules);

        Assert.Equal(forwards.Count, backwards.Count);
        Assert.Equal(forwards.SelectMany(a => a.Hits), backwards.SelectMany(a => a.Hits), RuleHitComparer.Instance);
    }

    [Fact]
    public void A_duplicated_event_has_no_effect()
    {
        var reset = Event(EventType.PasswordReset, Monday);
        var mfa = Event(EventType.MfaEnrolled, Monday.AddMinutes(4));

        var once = Assert.Single(Detector.Detect([reset, mfa], Rules));
        var twice = Assert.Single(Detector.Detect([reset, mfa, reset, mfa], Rules));

        Assert.Equal(once.Score, twice.Score);
        Assert.Equal(once.Hits, twice.Hits, RuleHitComparer.Instance);
    }

    [Fact]
    public void A_rule_that_fires_repeatedly_counts_once_and_keeps_its_first_evidence()
    {
        var firstReset = Event(EventType.PasswordReset, Monday);
        var firstMfa = Event(EventType.MfaEnrolled, Monday.AddMinutes(4));
        var secondReset = Event(EventType.PasswordReset, Monday.AddHours(5));
        var secondMfa = Event(EventType.MfaEnrolled, Monday.AddHours(5).AddMinutes(2));

        var alert = Assert.Single(Detector.Detect([firstReset, firstMfa, secondReset, secondMfa], Rules));

        Assert.Equal(30, alert.Score);
        var hit = Assert.Single(alert.Hits);
        Assert.Equal([firstReset.EventId, firstMfa.EventId], hit.EvidenceEventIds);
    }

    [Fact]
    public void Hits_from_different_rules_add_up_to_one_alert()
    {
        // Margaret's takeover: reset, new MFA, new email, new bank details, then a sale.
        var reset = Event(EventType.PasswordReset, Monday);
        var mfa = Event(EventType.MfaEnrolled, Monday.AddMinutes(4));
        var email = Event(EventType.EmailChanged, Monday.AddMinutes(10));
        var bank = Event(EventType.BankDetailsChanged, Monday.AddDays(1));
        var sale = Event(EventType.SaleRequested, Monday.AddDays(2));

        var alert = Assert.Single(Detector.Detect([reset, mfa, email, bank, sale], Rules));

        Assert.Equal(["R1", "R2", "R3", "R4"], alert.Hits.Select(h => h.RuleId));
        Assert.Equal(120, alert.Score);
        Assert.Equal(RiskBand.Critical, alert.Band);
        Assert.Equal([email.EventId, bank.EventId], alert.Hits.Single(h => h.RuleId == "R3").EvidenceEventIds);
    }

    [Fact]
    public void A_resent_event_with_different_details_is_ignored_like_the_event_store_ignores_it()
    {
        var reset = Event(EventType.PasswordReset, Monday);
        var mfa = Event(EventType.MfaEnrolled, Monday.AddHours(2));

        // Same EventId, but now claiming the reset was ten minutes before the MFA enrolment.
        var resent = reset with { OccurredAt = Monday.AddHours(2).AddMinutes(-10) };

        Assert.Empty(Detector.Detect([reset, mfa, resent], Rules));
    }

    [Fact]
    public void A_hit_more_than_the_risk_period_after_the_previous_one_opens_a_new_alert()
    {
        var reset = Event(EventType.PasswordReset, Monday);
        var mfa = Event(EventType.MfaEnrolled, Monday.AddMinutes(4));
        var email = Event(EventType.EmailChanged, Monday.AddMinutes(10));
        var bank = Event(EventType.BankDetailsChanged, Monday.AddMinutes(4).AddDays(7).AddSeconds(1));
        var sale = Event(EventType.SaleRequested, bank.OccurredAt.AddHours(1));

        // R3 (email → bank) cannot hit: its 72h window has passed, so the next hit is R4 at the sale.
        var alerts = Detector.Detect([reset, mfa, email, bank, sale], Rules);

        Assert.Collection(alerts,
            first => Assert.Equal(["R1", "R2"], first.Hits.Select(h => h.RuleId)),
            second =>
            {
                Assert.Equal(["R4"], second.Hits.Select(h => h.RuleId));
                Assert.Equal(40, second.Score);
                Assert.Equal(RiskBand.Medium, second.Band);
            });
    }

    [Fact]
    public void A_hit_exactly_the_risk_period_after_the_previous_one_joins_its_alert()
    {
        var reset = Event(EventType.PasswordReset, Monday);
        var mfa = Event(EventType.MfaEnrolled, Monday.AddMinutes(4));
        var bank = Event(EventType.BankDetailsChanged, Monday.AddDays(6));
        var sale = Event(EventType.SaleRequested, Monday.AddMinutes(4).AddDays(7));

        var alert = Assert.Single(Detector.Detect([reset, mfa, bank, sale], Rules));

        Assert.Equal(["R1", "R4"], alert.Hits.Select(h => h.RuleId));
        Assert.Equal(70, alert.Score);
    }

    [Fact]
    public void An_episode_longer_than_the_risk_period_keeps_its_score()
    {
        // Each hit is within 7 days of the one before, so the episode runs 12 days and nothing decays.
        var reset = Event(EventType.PasswordReset, Monday);
        var mfa = Event(EventType.MfaEnrolled, Monday.AddMinutes(4));
        var email = Event(EventType.EmailChanged, Monday.AddDays(5));
        var bank = Event(EventType.BankDetailsChanged, Monday.AddDays(6));
        var sale = Event(EventType.SaleRequested, Monday.AddDays(12));

        var alert = Assert.Single(Detector.Detect([reset, mfa, email, bank, sale], Rules));

        Assert.Equal(["R1", "R3", "R4"], alert.Hits.Select(h => h.RuleId));
        Assert.Equal(100, alert.Score);
        Assert.Equal(RiskBand.Critical, alert.Band);
    }

    [Fact]
    public void A_rule_counts_once_per_episode_and_again_in_a_new_one()
    {
        var firstReset = Event(EventType.PasswordReset, Monday);
        var firstMfa = Event(EventType.MfaEnrolled, Monday.AddMinutes(4));
        var secondReset = Event(EventType.PasswordReset, Monday.AddDays(10));
        var secondMfa = Event(EventType.MfaEnrolled, Monday.AddDays(10).AddMinutes(2));

        var alerts = Detector.Detect([firstReset, firstMfa, secondReset, secondMfa], Rules);

        Assert.Collection(alerts,
            a => Assert.Equal([firstReset.EventId, firstMfa.EventId], Assert.Single(a.Hits).EvidenceEventIds),
            a => Assert.Equal([secondReset.EventId, secondMfa.EventId], Assert.Single(a.Hits).EvidenceEventIds));
    }

    [Fact]
    public void Replaying_a_timeline_in_any_arrival_order_gives_identical_alerts()
    {
        PortalEvent[] timeline =
        [
            Event(EventType.PasswordReset, Monday),
            Event(EventType.MfaEnrolled, Monday.AddMinutes(4)),
            Event(EventType.EmailChanged, Monday.AddMinutes(10)),
            Event(EventType.BankDetailsChanged, Monday.AddDays(1)),
            Event(EventType.PasswordReset, Monday.AddDays(20)),
            Event(EventType.MfaEnrolled, Monday.AddDays(20).AddMinutes(30)),
            Event(EventType.PhoneChanged, Monday.AddDays(20).AddHours(2)),
        ];
        var expected = Detector.Detect(timeline, Rules);

        var random = new Random(13);
        for (var run = 0; run < 20; run++)
        {
            var shuffled = timeline.ToArray();
            random.Shuffle(shuffled);

            var actual = Detector.Detect(shuffled, Rules);

            Assert.Equal(expected.Count, actual.Count);
            foreach (var (e, a) in expected.Zip(actual))
            {
                Assert.Equal(e.Score, a.Score);
                Assert.Equal(e.Hits, a.Hits, RuleHitComparer.Instance);
            }
        }

        Assert.Equal(2, expected.Count);
    }

    [Fact]
    public void A_late_event_that_would_be_an_earlier_hit_for_an_already_hit_rule_changes_nothing()
    {
        var reset = Event(EventType.PasswordReset, Monday);
        var mfa = Event(EventType.MfaEnrolled, Monday.AddMinutes(20));
        var stored = Detector.Detect([reset, mfa], Rules);

        // Enrolled five minutes after the reset, but delayed at the source.
        var lateMfa = Event(EventType.MfaEnrolled, Monday.AddMinutes(5));
        var alert = Assert.Single(Detector.Detect([reset, mfa, lateMfa], Rules, stored));

        var hit = Assert.Single(alert.Hits);
        Assert.Equal([reset.EventId, mfa.EventId], hit.EvidenceEventIds);
        Assert.Equal(30, alert.Score);
    }

    [Fact]
    public void Detecting_again_over_the_same_timeline_leaves_stored_alerts_unchanged()
    {
        PortalEvent[] timeline =
        [
            Event(EventType.PasswordReset, Monday),
            Event(EventType.MfaEnrolled, Monday.AddMinutes(4)),
            Event(EventType.BankDetailsChanged, Monday.AddDays(6)),
            Event(EventType.SaleRequested, Monday.AddDays(20)),
            Event(EventType.PasswordReset, Monday.AddDays(20)),
            Event(EventType.MfaEnrolled, Monday.AddDays(20).AddMinutes(1)),
        ];
        var stored = Detector.Detect(timeline, Rules);

        var again = Detector.Detect(timeline, Rules, stored);

        Assert.Equal(stored.Count, again.Count);
        foreach (var (s, a) in stored.Zip(again)) Assert.Equal(s.Hits, a.Hits, RuleHitComparer.Instance);
    }

    [Fact]
    public void A_late_hit_for_a_new_rule_is_appended_after_the_stored_hits()
    {
        var reset = Event(EventType.PasswordReset, Monday);
        var mfa = Event(EventType.MfaEnrolled, Monday.AddMinutes(20));
        var stored = Detector.Detect([reset, mfa], Rules);

        var lateEmail = Event(EventType.EmailChanged, Monday.AddMinutes(10));
        var alert = Assert.Single(Detector.Detect([reset, mfa, lateEmail], Rules, stored));

        Assert.Equal(["R1", "R2"], alert.Hits.Select(h => h.RuleId));
        Assert.Equal(50, alert.Score);
    }

    [Fact]
    public void A_late_hit_just_before_a_stored_episode_joins_it_rather_than_opening_another()
    {
        // Over 24h after the reset below, so R2 (reset → contact change) stays out of it.
        var email = Event(EventType.EmailChanged, Monday.AddDays(1).AddHours(1));
        var bank = Event(EventType.BankDetailsChanged, Monday.AddDays(1).AddHours(2));
        var stored = Detector.Detect([email, bank], Rules);

        // The reset and MFA enrolment happened the day before but reached Vigil after the bank change.
        var reset = Event(EventType.PasswordReset, Monday);
        var mfa = Event(EventType.MfaEnrolled, Monday.AddMinutes(4));
        var alert = Assert.Single(Detector.Detect([email, bank, reset, mfa], Rules, stored));

        Assert.Equal(["R3", "R1"], alert.Hits.Select(h => h.RuleId));
        Assert.Equal(60, alert.Score);
        Assert.Equal(RiskBand.High, alert.Band);
    }

    [Fact]
    public void A_stored_alert_keeps_the_rules_version_that_opened_it()
    {
        var reset = Event(EventType.PasswordReset, Monday);
        var mfa = Event(EventType.MfaEnrolled, Monday.AddMinutes(4));
        var stored = Detector.Detect([reset, mfa], Rules with { Version = "v0" });

        var email = Event(EventType.EmailChanged, Monday.AddMinutes(10));
        var alert = Assert.Single(Detector.Detect([reset, mfa, email], Rules, stored));

        Assert.Equal("v0", alert.RulesVersion);
    }

    private static PortalEvent Login(DateTimeOffset at, string device, string country) =>
        Event(EventType.LoginSucceeded, at) with { DeviceId = device, Country = country };

    [Fact]
    public void An_accounts_first_event_is_no_first_sighting_because_there_is_no_baseline_to_compare_against()
    {
        Assert.Empty(Detector.Detect([Login(Monday, "D-77", "RO")], Rules));
    }

    [Fact]
    public void An_event_from_a_device_not_seen_before_on_the_account_is_a_first_sighting()
    {
        var usual = Login(Monday, "D-1", "GB");
        var reset = Event(EventType.PasswordReset, Monday.AddDays(30)) with { DeviceId = "D-77" };

        var hit = Assert.Single(Assert.Single(Detector.Detect([usual, reset], Rules)).Hits);

        Assert.Equal("R0", hit.RuleId);
        Assert.Equal(10, hit.Points);
        Assert.Equal([reset.EventId], hit.EvidenceEventIds);
        Assert.Equal(reset.OccurredAt, hit.TriggeredAt);
        Assert.Equal("First sighting on this Account: new device D-77", hit.Explanation);
    }

    [Fact]
    public void An_event_from_a_country_not_seen_before_on_the_account_is_a_first_sighting()
    {
        // The Holder's own laptop, taken abroad.
        var usual = Login(Monday, "D-1", "GB");
        var abroad = Login(Monday.AddDays(30), "D-1", "FR");

        var hit = Assert.Single(Assert.Single(Detector.Detect([usual, abroad], Rules)).Hits);

        Assert.Equal("R0", hit.RuleId);
        Assert.Equal([abroad.EventId], hit.EvidenceEventIds);
        Assert.Equal("First sighting on this Account: new country FR", hit.Explanation);
    }

    [Fact]
    public void A_new_device_and_a_new_country_on_the_same_event_are_one_first_sighting()
    {
        var usual = Login(Monday, "D-1", "GB");
        var stranger = Login(Monday.AddDays(30), "D-77", "RO");

        var alert = Assert.Single(Detector.Detect([usual, stranger], Rules));

        var hit = Assert.Single(alert.Hits);
        Assert.Equal(10, alert.Score);
        Assert.Equal([stranger.EventId], hit.EvidenceEventIds);
        Assert.Equal("First sighting on this Account: new device D-77, new country RO", hit.Explanation);
    }

    // RuleHit holds its evidence in a list, which records compare by reference.
    private sealed class RuleHitComparer : IEqualityComparer<RuleHit>
    {
        public static readonly RuleHitComparer Instance = new();

        public bool Equals(RuleHit? x, RuleHit? y) =>
            x is not null && y is not null
            && (x.RuleId, x.Points, x.TriggeredAt, x.Explanation) == (y.RuleId, y.Points, y.TriggeredAt, y.Explanation)
            && x.EvidenceEventIds.SequenceEqual(y.EvidenceEventIds);

        public int GetHashCode(RuleHit hit) => hit.RuleId.GetHashCode();
    }

    [Theory]
    [InlineData(0, RiskBand.Low)]
    [InlineData(29, RiskBand.Low)]
    [InlineData(30, RiskBand.Medium)]
    [InlineData(59, RiskBand.Medium)]
    [InlineData(60, RiskBand.High)]
    [InlineData(89, RiskBand.High)]
    [InlineData(90, RiskBand.Critical)]
    public void Band_thresholds_come_from_the_rules_file(int score, RiskBand expected) =>
        Assert.Equal(expected, Rules.Bands.BandFor(score));

    [Fact]
    public void A_sequence_rule_without_a_window_fails_to_load()
    {
        const string json = """
            { "version": "v1", "riskPeriodDays": 7, "bands": { "medium": 30, "high": 60, "critical": 90 },
              "rules": [{ "id": "R1", "name": "Recovery takeover", "kind": "sequence",
                          "steps": [["PasswordReset"], ["MfaEnrolled"]], "points": 30, "explanation": "x" }] }
            """;

        Assert.Throws<InvalidDataException>(() => RuleSet.Parse(json));
    }
}
