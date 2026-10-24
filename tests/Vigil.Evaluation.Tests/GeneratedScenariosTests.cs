using Vigil.Domain.Datasets;
using Vigil.Simulator.Scenarios;

namespace Vigil.Evaluation.Tests;

public sealed class GeneratedScenariosTests
{
    private const string Fraud = """{ "kind": "Ato", "intent": "Waits out R1's Window." }""";

    // One Generated Scenario object as the generator writes it; steps are "after:type" pairs, all by the fraudster.
    private static string Generated(string name, string steps, string? label = Fraud) => $$"""
        {
          "name": "{{name}}",
          "description": "",
          "accountId": "ACC-{{name.ToUpperInvariant()}}",
          {{(label is null ? "" : $"\"label\": {label},")}}
          "baseline": { "device": "D-1", "ip": "203.0.113.1", "country": "GB" },
          "actors": { "fraudster": { "device": "D-77", "ip": "198.51.100.77", "country": "RO" } },
          "steps": [{{string.Join(", ", steps.Split(' ').Select(s => s.Split(':')).Select(p =>
              $$"""{ "after": "{{p[0]}}", "type": "{{p[1]}}", "as": "fraudster" }"""))}}]
        }
        """;

    private static Scenario Parse(string json) => Scenario.Parse(json, "test.json");

    private static string Reply(params string[] scenarios) => $$"""{ "scenarios": [{{string.Join(", ", scenarios)}}] }""";

    [Fact]
    public void The_signature_is_the_ordered_event_types_and_the_gaps_rounded_to_the_hour()
    {
        var scenario = Parse(Generated("slow", "0m:PasswordReset 1h29m:MfaEnrolled 1d2h:EmailChanged"));

        Assert.Equal("PasswordReset +1h MfaEnrolled +25h EmailChanged", GeneratedScenarios.Signature(scenario));
    }

    [Fact]
    public void Scenarios_that_differ_only_in_minutes_names_and_actors_share_a_signature()
    {
        var first = Parse(Generated("one", "0m:PasswordReset 65m:MfaEnrolled"));
        var second = Parse(Generated("two", "10m:PasswordReset 1h20m:MfaEnrolled"));

        Assert.Equal(GeneratedScenarios.Signature(first), GeneratedScenarios.Signature(second));
    }

    [Fact]
    public void A_valid_labelled_scenario_is_kept_as_JSON_that_parses_back_to_it()
    {
        var result = GeneratedScenarios.Accept(Reply(Generated("waits-out-r1", "0m:PasswordReset 65m:MfaEnrolled")), existing: []);

        var kept = Assert.Single(result.Kept);
        Assert.Empty(result.Rejected);
        Assert.Equal("waits-out-r1", kept.Scenario.Name);
        Assert.Equal(new ScenarioLabel(FraudKind.Ato, "Waits out R1's Window."), Parse(kept.Json).Label);
        Assert.Equal("PasswordReset +1h MfaEnrolled", GeneratedScenarios.Signature(Parse(kept.Json)));
    }

    [Fact]
    public void A_scenario_without_a_label_is_rejected()
    {
        var result = GeneratedScenarios.Accept(Reply(Generated("unlabelled", "0m:PasswordReset", label: null)), existing: []);

        Assert.Empty(result.Kept);
        Assert.Contains("label", Assert.Single(result.Rejected));
    }

    [Fact]
    public void A_scenario_the_strict_parser_refuses_is_rejected_with_its_reason_and_the_rest_are_kept()
    {
        var result = GeneratedScenarios.Accept(
            Reply(Generated("typo", "0m:PasswordRest"), Generated("fine", "0m:PasswordReset")), existing: []);

        Assert.Equal(["fine"], result.Kept.Select(k => k.Scenario.Name));
        Assert.Contains("PasswordRest", Assert.Single(result.Rejected));
    }

    [Fact]
    public void A_scenario_shaped_like_one_already_kept_is_rejected_as_a_duplicate()
    {
        var existing = Parse(Generated("earlier", "0m:PasswordReset 61m:MfaEnrolled"));

        var result = GeneratedScenarios.Accept(Reply(Generated("again", "0m:PasswordReset 70m:MfaEnrolled")), [existing]);

        Assert.Empty(result.Kept);
        Assert.Contains("earlier", Assert.Single(result.Rejected));
    }

    [Fact]
    public void Existing_scenarios_may_share_a_shape_with_each_other()
    {
        var existing = new[] { Parse(Generated("derek", "0m:PasswordReset 6m:MfaEnrolled")), Parse(Generated("lena", "0m:PasswordReset 5m:MfaEnrolled")) };

        var result = GeneratedScenarios.Accept(Reply(Generated("new", "0m:EmailChanged")), existing);

        Assert.Single(result.Kept);
    }

    [Fact]
    public void Two_scenarios_of_the_same_shape_in_one_reply_keep_only_the_first()
    {
        var result = GeneratedScenarios.Accept(
            Reply(Generated("first", "0m:PasswordReset 65m:MfaEnrolled"), Generated("second", "0m:PasswordReset 66m:MfaEnrolled")), existing: []);

        Assert.Equal(["first"], result.Kept.Select(k => k.Scenario.Name));
        Assert.Contains("first", Assert.Single(result.Rejected));
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("Upper Case")]
    public void A_name_that_is_not_a_safe_file_name_is_rejected(string name)
    {
        var result = GeneratedScenarios.Accept(Reply(Generated(name, "0m:PasswordReset")), existing: []);

        Assert.Empty(result.Kept);
        Assert.Contains("name", Assert.Single(result.Rejected));
    }

    [Fact]
    public void A_name_already_used_by_another_shape_is_rejected_rather_than_overwriting_it()
    {
        var existing = Parse(Generated("taken", "0m:EmailChanged"));

        var result = GeneratedScenarios.Accept(Reply(Generated("taken", "0m:PasswordReset")), [existing]);

        Assert.Empty(result.Kept);
        Assert.Contains("taken", Assert.Single(result.Rejected));
    }

    [Fact]
    public void A_missing_folder_holds_no_Generated_Scenarios()
    {
        Assert.Empty(GeneratedScenarios.LoadAll(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())));
    }

    [Fact]
    public void A_file_in_the_folder_without_a_label_is_refused_rather_than_skipped()
    {
        var folder = Directory.CreateTempSubdirectory().FullName;
        File.WriteAllText(Path.Combine(folder, "a.json"), Generated("labelled", "0m:PasswordReset"));
        File.WriteAllText(Path.Combine(folder, "b.json"), Generated("hand-edited", "0m:PasswordReset", label: null));

        var error = Assert.Throws<InvalidDataException>(() => GeneratedScenarios.LoadAll(folder));
        Assert.Contains("hand-edited", error.Message);
    }

    [Fact]
    public void The_committed_Generated_Scenarios_include_at_least_one_Blind_Spot()
    {
        var generated = GeneratedScenarios.LoadAll(Path.Combine(AppContext.BaseDirectory, "datasets", "generated"));
        var rules = Vigil.Domain.Detection.RuleSet.Load(Path.Combine(AppContext.BaseDirectory, "config", "rules.v2.json"));

        Assert.NotEmpty(BlindSpots.Find(generated, rules).BlindSpots);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{ "other": [] }""")]
    public void A_reply_that_is_not_a_scenarios_object_throws(string reply)
    {
        Assert.Throws<InvalidDataException>(() => GeneratedScenarios.Accept(reply, existing: []));
    }
}
