using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Vigil.Agents;
using Vigil.Domain.Datasets;
using Vigil.Domain.Detection;
using Vigil.Simulator.Scenarios;

namespace Vigil.Evaluation.Tests;

public sealed class ScenarioGeneratorTests
{
    private static readonly string RulesV2 = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "config", "rules.v2.json"));
    private static readonly string Margaret = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "scenarios", "margaret-ato.json"));

    private const string OneScenario = """
        { "scenarios": [{
          "name": "late-mfa", "description": "", "accountId": "ACC-LATE",
          "label": { "kind": "Ato", "intent": "Waits out R1." },
          "baseline": { "device": "D-1", "ip": "203.0.113.1", "country": "GB" },
          "steps": [{ "after": "0m", "type": "PasswordReset" }, { "after": "2h", "type": "MfaEnrolled" }]
        }] }
        """;

    [Fact]
    public async Task The_generator_is_shown_the_rules_file_the_examples_and_the_shapes_already_kept_and_its_reply_is_accepted()
    {
        var chat = new RecordingChatClient(OneScenario);
        var existing = Scenario.Parse(Margaret, "margaret-ato.json");

        var run = await new ScenarioGenerator(chat).GenerateAsync(3, RulesV2, [Margaret], [existing], CancellationToken.None);

        Assert.Equal(["late-mfa"], run.Result.Kept.Select(k => k.Scenario.Name));
        Assert.Equal((1000, 100), (run.InputTokens, run.OutputTokens));
        var prompt = string.Join("\n", chat.Messages.Select(m => m.Text));
        Assert.Contains("\"windowMinutes\": 60", prompt);
        Assert.Contains("m.attacker@example.net", prompt);
        Assert.Contains(GeneratedScenarios.Signature(existing), prompt);
        Assert.Contains("Write 3 Generated Scenarios", prompt);
        Assert.Contains("the rules would miss", (chat.Options?.Instructions ?? "") + prompt);
    }

    [Fact]
    public async Task More_than_the_budgeted_count_is_refused_before_any_call()
    {
        var chat = new RecordingChatClient(OneScenario);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            new ScenarioGenerator(chat).GenerateAsync(ScenarioGenerator.MaxCount + 1, RulesV2, [], [], CancellationToken.None));
        Assert.Empty(chat.Messages);
    }

    [Fact]
    public async Task The_Fake_provider_writes_one_fraud_Scenario_that_v2_never_takes_to_High()
    {
        var run = await new ScenarioGenerator(new FakeChatClient()).GenerateAsync(5, RulesV2, [Margaret], [], CancellationToken.None);

        var kept = Assert.Single(run.Result.Kept);
        Assert.Equal(FraudKind.Ato, kept.Scenario.Label!.FraudKind);
        Assert.Contains("Fake", kept.Scenario.Label.Intent);
        var review = BlindSpots.Find([kept.Scenario], RuleSet.Load(Path.Combine(AppContext.BaseDirectory, "config", "rules.v2.json")));
        Assert.Single(review.BlindSpots);
    }

    /// <summary>Replies with one fixed text and keeps what it was sent. No test calls a real LLM.</summary>
    private sealed class RecordingChatClient(string reply) : IChatClient
    {
        public List<ChatMessage> Messages { get; } = [];
        public ChatOptions? Options { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Messages.AddRange(messages);
            Options = options;
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, reply))
            {
                Usage = new UsageDetails { InputTokenCount = 1000, OutputTokenCount = 100 },
            });
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var update in (await GetResponseAsync(messages, options, cancellationToken)).ToChatResponseUpdates())
                yield return update;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose() { }
    }
}
