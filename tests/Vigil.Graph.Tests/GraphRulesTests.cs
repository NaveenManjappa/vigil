using Vigil.Domain.Detection;
using Vigil.Domain.Events;

namespace Vigil.Graph.Tests;

public sealed class GraphRulesTests(Neo4jFixture neo4j) : IClassFixture<Neo4jFixture>, IAsyncLifetime
{
    private static readonly DateTimeOffset Monday = new(2026, 9, 21, 9, 0, 0, TimeSpan.Zero);
    private static readonly RuleSet V1 = RuleSet.Load(Path.Combine(AppContext.BaseDirectory, "config", "rules.v1.json"));
    private static readonly RuleSet V2 = RuleSet.Load(Path.Combine(AppContext.BaseDirectory, "config", "rules.v2.json"));
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly GraphProjector _projector = new(neo4j.Driver);
    private readonly GraphRules _rules = new(neo4j.Driver);

    // Each test starts from an empty graph, so only its own links can produce a hit.
    public async ValueTask InitializeAsync() =>
        await neo4j.Driver.ExecutableQuery("MATCH (n) DETACH DELETE n").ExecuteAsync(Ct);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static PortalEvent BankChange(string account, DateTimeOffset at, string bankAccount, string? holder = null) =>
        new(Guid.NewGuid(), account, holder ?? $"HOLDER-{account}", EventType.BankDetailsChanged, at, $"D-{account}", "198.51.100.1", "GB", "web",
            new Dictionary<string, string> { ["NewBankAccountHash"] = bankAccount });

    private Task ProjectAsync(params PortalEvent[] events) => _projector.ProjectAsync(events, Ct);

    [Fact]
    public async Task A_bank_account_shared_with_another_Holders_Account_is_G1()
    {
        var first = BankChange("ACC-1", Monday, "BA-9f31");
        var second = BankChange("ACC-2", Monday.AddDays(2), "BA-9f31");
        await ProjectAsync(first);
        await ProjectAsync(second);

        var hit = Assert.Single(await _rules.HitsForAsync("ACC-2", V2, Ct));

        Assert.Equal("G1", hit.RuleId);
        Assert.Equal(30, hit.Points);
        // ACC-2's bank change completed the link, so it is the trigger and comes last.
        Assert.Equal(second.OccurredAt, hit.TriggeredAt);
        Assert.Equal([first.EventId, second.EventId], hit.EvidenceEventIds);
        Assert.Equal("Pays out to bank account BA-9f31, which ACC-1 of a different Holder also pays out to", hit.Explanation);
    }

    [Fact]
    public async Task The_Account_that_added_the_bank_account_first_is_triggered_by_the_later_link()
    {
        // The graph is read as it is now, so ACC-1 sees ACC-2's later link too.
        var first = BankChange("ACC-1", Monday, "BA-9f31");
        var second = BankChange("ACC-2", Monday.AddDays(2), "BA-9f31");
        await ProjectAsync(first, second);

        var hit = Assert.Single(await _rules.HitsForAsync("ACC-1", V2, Ct));

        Assert.Equal(second.OccurredAt, hit.TriggeredAt);
        Assert.Equal([first.EventId, second.EventId], hit.EvidenceEventIds);
        Assert.Equal("Pays out to bank account BA-9f31, which ACC-2 of a different Holder also pays out to", hit.Explanation);
    }

    [Fact]
    public async Task A_bank_account_shared_by_two_Accounts_of_the_same_Holder_is_not_G1()
    {
        await ProjectAsync(
            BankChange("ACC-1", Monday, "BA-9f31", holder: "HOLDER-1"),
            BankChange("ACC-2", Monday.AddDays(2), "BA-9f31", holder: "HOLDER-1"));

        Assert.Empty(await _rules.HitsForAsync("ACC-2", V2, Ct));
    }

    [Fact]
    public async Task A_bank_account_no_other_Account_uses_is_not_G1()
    {
        // Sharing a device is not sharing a bank account.
        await ProjectAsync(
            BankChange("ACC-1", Monday, "BA-9f31") with { DeviceId = "D-77" },
            BankChange("ACC-2", Monday.AddDays(2), "BA-0000") with { DeviceId = "D-77" });

        Assert.Empty(await _rules.HitsForAsync("ACC-2", V2, Ct));
    }

    [Fact]
    public async Task Each_other_Holders_Account_on_the_bank_account_is_a_candidate_hit()
    {
        // Detection keeps one per Risk Episode; the graph reports every link it can see.
        var one = BankChange("ACC-1", Monday, "BA-9f31");
        var two = BankChange("ACC-2", Monday.AddDays(1), "BA-9f31");
        var three = BankChange("ACC-3", Monday.AddDays(2), "BA-9f31");
        await ProjectAsync(one, two, three);

        var hits = await _rules.HitsForAsync("ACC-3", V2, Ct);

        Assert.Equivalent(
            new[] { new[] { one.EventId, three.EventId }, [two.EventId, three.EventId] },
            hits.Select(h => h.EvidenceEventIds));
        Assert.All(hits, h => Assert.Equal(three.OccurredAt, h.TriggeredAt));
    }

    [Fact]
    public async Task A_late_earlier_bank_change_becomes_the_evidence_for_the_link()
    {
        var later = BankChange("ACC-1", Monday.AddDays(1), "BA-9f31");
        var other = BankChange("ACC-2", Monday.AddDays(2), "BA-9f31");
        await ProjectAsync(later, other);

        // Delayed at the source: ACC-1 first paid to BA-9f31 the day before.
        var earlier = BankChange("ACC-1", Monday, "BA-9f31");
        await ProjectAsync(earlier);

        var hit = Assert.Single(await _rules.HitsForAsync("ACC-2", V2, Ct));
        Assert.Equal([earlier.EventId, other.EventId], hit.EvidenceEventIds);
    }

    [Fact]
    public async Task A_rule_set_without_graph_rules_gives_no_graph_hits()
    {
        await ProjectAsync(BankChange("ACC-1", Monday, "BA-9f31"), BankChange("ACC-2", Monday.AddDays(2), "BA-9f31"));

        Assert.Empty(await _rules.HitsForAsync("ACC-2", V1, Ct));
    }

    [Fact]
    public void A_graph_rule_with_no_query_behind_it_is_rejected()
    {
        var unknown = V2 with { Rules = [.. V2.Rules, new Rule("G9", "Unknown", RuleKind.Graph, 10, "x")] };

        Assert.Throws<InvalidDataException>(() => GraphRules.EnsureKnown(unknown));
    }
}
