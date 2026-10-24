using Vigil.Agents;
using Vigil.Domain.Datasets;
using Vigil.Domain.Detection;
using Vigil.Domain.Events;

namespace Vigil.Evaluation.Tests;

/// <summary>Two hand-made Accounts of an Agent Sample, and the Findings a scripted Investigator writes on them.</summary>
public static class AgentSamples
{
    public static readonly DateTimeOffset Start = new(2026, 9, 1, 9, 0, 0, TimeSpan.Zero);

    // £0.0003 and £0.0012 per 1,000 tokens, as the API's appsettings.json.
    public static readonly LlmOptions Prices = new() { InputPricePer1kTokens = 0.0003m, OutputPricePer1kTokens = 0.0012m };

    public static string Finding(string verdict, string evidenceRef) => $$"""
        {"verdict":"{{verdict}}","confidence":"High","summary":"A new device reset the password.",
         "recommendation":"ContactHolder","evidence":["{{evidenceRef}}"]}
        """;

    /// <summary>A Judge's reply, each reason naming its score so a test can tell the criteria apart.</summary>
    public static string Judgement(int grounded, int reasoning, int recommendation, int clarity) => $$"""
        {"groundedClaims":{"score":{{grounded}},"reason":"Scored {{grounded}}."},
         "soundReasoning":{"score":{{reasoning}},"reason":"Scored {{reasoning}}."},
         "proportionateRecommendation":{"score":{{recommendation}},"reason":"Scored {{recommendation}}."},
         "clarity":{"score":{{clarity}},"reason":"Scored {{clarity}}."}
        }
        """;

    /// <summary>A usual login, then a password reset from a new device that is the Alert's only evidence, so the brief shows E-1.</summary>
    public static (LabelledAccount Account, SampledAccount Sampled) Account(string id, Label label, bool isNearMiss)
    {
        PortalEvent Event(EventType type, int days, string device) =>
            new(Guid.NewGuid(), id, $"HOLDER-{id}", type, Start.AddDays(days), device, "198.51.100.1", "GB", "web", new Dictionary<string, string>());

        var reset = Event(EventType.PasswordReset, 10, "D-NEW");
        var account = new LabelledAccount(id, label, [Event(EventType.LoginSucceeded, 0, "D-HOME"), reset]);
        var alert = new Alert(id, 60, RiskBand.High, "v1", [new RuleHit("R1", 30, [reset.EventId], reset.OccurredAt, "Password reset from a new device")]);
        return (account, new SampledAccount(account, alert, isNearMiss));
    }

    public static readonly (LabelledAccount Account, SampledAccount Sampled) Ato = Account("ACC-ATO", new Label(Persona.AtoFraudster, FraudKind.Ato), false);
    public static readonly (LabelledAccount Account, SampledAccount Sampled) NearMiss = Account("ACC-NEAR", new Label(Persona.NewPhone, null), true);

    public static Task<AgentEvaluation> RunAsync(ScriptedChatClient chat, params (LabelledAccount Account, SampledAccount Sampled)[] sample) =>
        AgentEvaluator.RunAsync([.. sample.Select(s => s.Sampled)], [.. sample.Select(s => s.Account)], graph: null, chat, Prices,
            TestContext.Current.CancellationToken);
}
