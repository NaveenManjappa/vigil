using Vigil.Domain.Datasets;
using Vigil.Domain.Detection;
using Vigil.Domain.Events;

namespace Vigil.Evaluation.Tests;

/// <summary>The same throwaway Neo4j the evaluation console starts, shared by one test class.</summary>
public sealed class ThrowawayNeo4jFixture : IAsyncLifetime
{
    public ThrowawayNeo4j Neo4j { get; private set; } = null!;

    public async ValueTask InitializeAsync() => Neo4j = await ThrowawayNeo4j.StartAsync(CancellationToken.None);

    public async ValueTask DisposeAsync() => await Neo4j.DisposeAsync();
}

public sealed class GraphEvaluationTests(ThrowawayNeo4jFixture fixture) : IClassFixture<ThrowawayNeo4jFixture>
{
    private static readonly DateTimeOffset Start = new(2026, 9, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly RuleSet V1 = RuleSet.Load(Path.Combine(AppContext.BaseDirectory, "config", "rules.v1.json"));
    private static readonly RuleSet V2 = RuleSet.Load(Path.Combine(AppContext.BaseDirectory, "config", "rules.v2.json"));

    /// <summary>
    /// A Ring member: a login from its own device, then a day later per member a login from the ring's device D-RING,
    /// an email change and, a day after that, a bank change to the ring's BA-RING.
    /// </summary>
    private static LabelledAccount RingMember(int member)
    {
        var id = $"RING-{member}";
        var takeover = Start.AddDays(member);
        return new(id, new Label(Persona.FraudRing, FraudKind.Ring),
        [
            Event(id, EventType.LoginSucceeded, Start.AddDays(-30), $"D-{id}"),
            Event(id, EventType.LoginSucceeded, takeover, "D-RING"),
            Event(id, EventType.EmailChanged, takeover.AddMinutes(10), "D-RING", ("NewEmail", $"{id}@example.net")),
            Event(id, EventType.BankDetailsChanged, takeover.AddDays(1), "D-RING", ("NewBankAccountHash", "BA-RING")),
        ]);
    }

    private static PortalEvent Event(string account, EventType type, DateTimeOffset at, string device, params (string Key, string Value)[] attributes) =>
        new(Guid.NewGuid(), account, $"HOLDER-{account}", type, at, device, "198.51.100.1", "GB", "web",
            attributes.ToDictionary(a => a.Key, a => a.Value));

    // v1: R0 10 at the D-RING login + R3 30 at the bank change = 40, Medium, for every member.
    // v2 adds G1 30 (BA-RING is shared with another Holder) and G3 20 (D-RING and BA-RING are each on 3 Accounts,
    // counted once per episode) = 90, Critical. The first member's G1 and G3 land within 2 days of its bank change,
    // so they join its episode. The quiet Account shares the IP, which never links, so it stays without hits.
    private static readonly LabelledAccount[] HandLabelled =
    [
        RingMember(1),
        RingMember(2),
        RingMember(3),
        new("QUIET", new Label(Persona.QuietHolder, null), [Event("QUIET", EventType.LoginSucceeded, Start, "D-QUIET")]),
    ];

    [Fact]
    public async Task A_Ring_is_flagged_with_the_graph_on_but_not_off()
    {
        var off = Evaluator.Evaluate(HandLabelled, V1);
        var on = Evaluator.Evaluate(HandLabelled, V2, await GraphReplay.CollectAsync(fixture.Neo4j.Driver, HandLabelled, V2, CancellationToken.None));

        Assert.Equal(new Rate(0, 3), off.DetectionRate);
        Assert.Equal(new Rate(3, 3), on.DetectionRate);
        Assert.Equal(new Rate(0, 1), on.FalsePositiveRate);

        var ring = on.Personas.Single(p => p.Persona == Persona.FraudRing);
        Assert.Equal(3, ring.PeakBands[RiskBand.Critical]);
    }

    [Fact]
    public async Task Without_Confirmed_Fraud_G2_has_no_hits()
    {
        var on = Evaluator.Evaluate(HandLabelled, V2, await GraphReplay.CollectAsync(fixture.Neo4j.Driver, HandLabelled, V2, CancellationToken.None));

        Assert.Equal(
            [("G1", new Rate(3, 3)), ("G2", new Rate(0, 0)), ("G3", new Rate(3, 3))],
            on.RulePrecision.Where(p => p.RuleId.StartsWith('G')).Select(p => (p.RuleId, p.Precision)));
    }
}
