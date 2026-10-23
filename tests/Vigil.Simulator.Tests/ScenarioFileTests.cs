using Vigil.Domain.Events;
using Vigil.Domain.Fraud;
using Vigil.Simulator.Scenarios;

namespace Vigil.Simulator.Tests;

public sealed class ScenarioFileTests
{
    private static readonly string Folder = Path.Combine(AppContext.BaseDirectory, "scenarios");
    private static readonly DateTimeOffset Monday = new(2026, 9, 21, 9, 0, 0, TimeSpan.Zero);

    // Steps are added to this so each error test changes only the part it is about.
    private static string Json(
        string steps, string baseline = """{ "device": "D-1", "ip": "203.0.113.1", "country": "GB" }""", string extra = "") => $$"""
        {
          {{extra}}
          "name": "test",
          "description": "",
          "accountId": "ACC-TEST",
          "baseline": {{baseline}},
          "actors": { "fraudster": { "device": "D-77", "ip": "198.51.100.77", "country": "RO" } },
          "steps": [{{steps}}]
        }
        """;

    [Fact]
    public void Margarets_file_keeps_the_EventIds_and_times_the_simulator_has_always_posted()
    {
        var scenario = Scenario.Load(Path.Combine(Folder, "margaret-ato.json"));

        Assert.Equal(13, scenario.Baseline.Count);
        Assert.Equal(DeterministicGuid.Create("margaret-ato/baseline/1"), scenario.Baseline[0].EventId);
        Assert.Equal(Monday.AddDays(-91), scenario.Baseline[0].OccurredAt);
        Assert.Equal(Monday.AddDays(-7), scenario.Baseline[^1].OccurredAt);
        Assert.All(scenario.Baseline, e => Assert.Equal(("D-MARGARET-LAPTOP", "GB"), (e.DeviceId, e.Country)));

        Assert.Equal(
            [EventType.PasswordReset, EventType.MfaEnrolled, EventType.EmailChanged, EventType.BankDetailsChanged],
            scenario.Steps.Select(s => s.Event.Type));
        Assert.Equal(
            [Monday, Monday.AddMinutes(4), Monday.AddMinutes(10), Monday.AddDays(1).AddHours(5)],
            scenario.Steps.Select(s => s.Event.OccurredAt));
        Assert.Equal(
            Enumerable.Range(1, 4).Select(n => DeterministicGuid.Create($"margaret-ato/{n}")),
            scenario.Steps.Select(s => s.Event.EventId));
        Assert.All(scenario.Steps, s => Assert.Equal(("fraudster", "D-77", "RO"), (s.Actor, s.Event.DeviceId, s.Event.Country)));
        Assert.Equal("m.attacker@example.net", scenario.Steps[2].Event.Attributes["NewEmail"]);
    }

    [Fact]
    public void A_step_with_no_actor_is_the_holder_on_the_baseline_device()
    {
        var scenario = Scenario.Load(Path.Combine(Folder, "patel-house-move.json"));

        Assert.All(scenario.Steps, s =>
            Assert.Equal(("holder", "D-PATEL-LAPTOP", "203.0.113.24", "GB"), (s.Actor, s.Event.DeviceId, s.Event.IpAddress, s.Event.Country)));
        Assert.Equal(Monday.AddDays(2), scenario.Steps[1].Event.OccurredAt);
    }

    [Fact]
    public void Every_file_in_the_folder_loads()
    {
        Assert.Equal(["margaret-ato", "patel-house-move"], Scenario.LoadAll(Folder).Select(s => s.Name));
    }

    [Fact]
    public void Events_are_the_baseline_then_the_steps()
    {
        var scenario = Scenario.Parse(Json("""{ "after": "1h", "type": "PasswordReset" }"""), "test.json");

        Assert.Equal([.. scenario.Baseline, scenario.Steps[0].Event], scenario.Events());
    }

    [Fact]
    public void A_scenario_without_a_baseline_is_a_new_Account()
    {
        var scenario = Scenario.Parse(Json("""{ "after": "0m", "type": "AccountRegistered", "as": "fraudster" }""", baseline: "null"), "test.json");

        Assert.Empty(scenario.Baseline);
        Assert.Single(scenario.Events());
    }

    [Theory]
    [InlineData("""{ "after": "1h", "type": "PasswordRest" }""", "step 1", "PasswordRest")]
    [InlineData("""{ "after": "1h", "type": "3" }""", "step 1", "3")]
    [InlineData("""{ "after": "1h", "type": "PasswordReset", "as": "stranger" }""", "step 1", "stranger")]
    [InlineData("""{ "after": "1h", "type": "PasswordReset" }, { "after": "ten minutes", "type": "MfaEnrolled" }""", "step 2", "ten minutes")]
    [InlineData("""{ "after": "", "type": "PasswordReset" }""", "step 1", "after")]
    public void A_bad_step_is_rejected_naming_the_file_and_step(string steps, string step, string culprit)
    {
        var error = Assert.Throws<InvalidDataException>(() => Scenario.Parse(Json(steps), "test.json"));

        Assert.Contains("test.json", error.Message);
        Assert.Contains(step, error.Message);
        Assert.Contains(culprit, error.Message);
    }

    [Fact]
    public void A_step_without_an_actor_needs_a_baseline_holder()
    {
        var error = Assert.Throws<InvalidDataException>(() =>
            Scenario.Parse(Json("""{ "after": "0m", "type": "AccountRegistered" }""", baseline: "null"), "test.json"));

        Assert.Contains("step 1", error.Message);
    }

    [Fact]
    public void A_misspelt_field_is_rejected_rather_than_silently_ignored()
    {
        Assert.Throws<InvalidDataException>(() => Scenario.Parse(Json("""{ "afer": "1h", "type": "PasswordReset" }"""), "test.json"));
    }

    [Fact]
    public void A_confirmedFraud_date_marks_the_Account_as_seeded_Confirmed_Fraud()
    {
        var scenario = Scenario.Parse(
            Json("""{ "after": "0m", "type": "LoginSucceeded" }""", extra: """ "confirmedFraud": "2026-09-14T12:00:00Z", """), "test.json");

        Assert.Equal(
            new ConfirmedFraud("ACC-TEST", new DateTimeOffset(2026, 9, 14, 12, 0, 0, TimeSpan.Zero), ConfirmedFraudSource.Seed),
            scenario.ConfirmedFraud);
    }

    [Fact]
    public void A_scenario_without_confirmedFraud_is_not_Confirmed_Fraud()
    {
        Assert.Null(Scenario.Parse(Json("""{ "after": "0m", "type": "LoginSucceeded" }"""), "test.json").ConfirmedFraud);
    }

    [Fact]
    public void A_misspelt_confirmedFraud_is_rejected()
    {
        Assert.Throws<InvalidDataException>(() => Scenario.Parse(
            Json("""{ "after": "0m", "type": "LoginSucceeded" }""", extra: """ "confirmedFruad": "2026-09-14T12:00:00Z", """), "test.json"));
    }
}
