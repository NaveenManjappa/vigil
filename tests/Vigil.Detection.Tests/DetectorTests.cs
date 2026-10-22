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
}
