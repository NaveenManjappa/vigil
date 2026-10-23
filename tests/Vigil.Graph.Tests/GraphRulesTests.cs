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

    private static Dictionary<string, string> Attribute(string key, string value) => new() { [key] = value };

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
    public async Task Neighbours_are_the_other_Accounts_linked_to_the_batchs_Linking_Identifiers()
    {
        // Every event here comes from IP 198.51.100.1, so ACC-IP-ONLY would be a neighbour if IPs linked.
        await ProjectAsync(
            BankChange("ACC-BANK", Monday, "BA-9f31"),
            BankChange("ACC-DEVICE", Monday, "BA-0001") with { DeviceId = "D-77" },
            BankChange("ACC-EMAIL", Monday, "BA-0002") with { Attributes = Attribute("NewEmail", "x@example.com") },
            BankChange("ACC-PHONE", Monday, "BA-0003") with { Attributes = Attribute("NewPhone", "+447700900123") },
            BankChange("ACC-IP-ONLY", Monday, "BA-0004"),
            BankChange("ACC-OTHER-BATCH", Monday, "BA-0005"));

        PortalEvent[] batch =
        [
            BankChange("ACC-NEW", Monday.AddDays(1), "BA-9f31") with { DeviceId = "D-77" },
            BankChange("ACC-NEW", Monday.AddDays(1), "BA-0006") with { Attributes = Attribute("NewEmail", "x@example.com") },
            BankChange("ACC-NEW", Monday.AddDays(1), "BA-0007") with { Attributes = Attribute("NewPhone", "+447700900123") },
            // The batch's own Accounts are scored anyway, so one linked to another is not its neighbour.
            BankChange("ACC-OTHER-BATCH", Monday.AddDays(1), "BA-9f31"),
        ];
        await ProjectAsync(batch);

        Assert.Equal(["ACC-BANK", "ACC-DEVICE", "ACC-EMAIL", "ACC-PHONE"], await _rules.NeighboursOfAsync(batch, Ct));
    }

    // Each Account gets its own bank account unless one is given, so only the device links them.
    private static PortalEvent Linked(string account, DateTimeOffset at, string? device = null, string? bankAccount = null) =>
        BankChange(account, at, bankAccount ?? $"BA-{account}") with { DeviceId = device ?? $"D-{account}" };

    private async Task<List<RuleHit>> RingHitsAsync(string accountId, RuleSet? ruleSet = null) =>
        [.. (await _rules.HitsForAsync(accountId, ruleSet ?? V2, Ct)).Where(h => h.RuleId == "G3")];

    private static RuleSet WithRingThreshold(int threshold) =>
        V2 with { Rules = [.. V2.Rules.Select(r => r.Id == "G3" ? r with { AccountThreshold = threshold } : r)] };

    [Fact]
    public async Task A_device_shared_by_three_Accounts_is_G3_for_the_third()
    {
        var one = Linked("ACC-1", Monday, device: "D-77");
        var two = Linked("ACC-2", Monday.AddDays(1), device: "D-77");
        var three = Linked("ACC-3", Monday.AddDays(2), device: "D-77");
        await ProjectAsync(one, two, three);

        var hit = Assert.Single(await RingHitsAsync("ACC-3"));

        Assert.Equal(20, hit.Points);
        // ACC-3's link brought D-77 to the threshold of 3, so it is the trigger and comes last.
        Assert.Equal(three.OccurredAt, hit.TriggeredAt);
        Assert.Equal([one.EventId, two.EventId, three.EventId], hit.EvidenceEventIds);
        Assert.Equal("Device D-77 is shared by 3 Accounts; Ring threshold 3", hit.Explanation);
    }

    [Fact]
    public async Task A_bank_account_shared_by_three_Accounts_is_G3()
    {
        var one = Linked("ACC-1", Monday, bankAccount: "BA-9f31");
        var two = Linked("ACC-2", Monday.AddDays(1), bankAccount: "BA-9f31");
        var three = Linked("ACC-3", Monday.AddDays(2), bankAccount: "BA-9f31");
        await ProjectAsync(one, two, three);

        var hit = Assert.Single(await RingHitsAsync("ACC-3"));

        Assert.Equal([one.EventId, two.EventId, three.EventId], hit.EvidenceEventIds);
        Assert.Equal("BankAccount BA-9f31 is shared by 3 Accounts; Ring threshold 3", hit.Explanation);
    }

    [Fact]
    public async Task A_device_or_bank_account_shared_by_two_Accounts_is_not_G3()
    {
        await ProjectAsync(
            Linked("ACC-1", Monday, device: "D-77", bankAccount: "BA-9f31"),
            Linked("ACC-2", Monday.AddDays(1), device: "D-77", bankAccount: "BA-9f31"));

        Assert.Empty(await RingHitsAsync("ACC-2"));
    }

    [Fact]
    public async Task A_shared_IP_or_Address_alone_is_never_G3()
    {
        // Every BankChange comes from IP 198.51.100.1; the postcode is added on top. Devices and bank accounts all differ.
        var postcode = Attribute("NewPostcode", "EC1A 1BB");
        await ProjectAsync(
            Linked("ACC-1", Monday) with { Attributes = postcode },
            Linked("ACC-2", Monday.AddDays(1)) with { Attributes = postcode },
            Linked("ACC-3", Monday.AddDays(2)) with { Attributes = postcode });

        Assert.Empty(await RingHitsAsync("ACC-3"));
    }

    [Fact]
    public async Task Every_Account_on_the_identifier_is_G3_once_the_threshold_is_reached()
    {
        var one = Linked("ACC-1", Monday, device: "D-77");
        var two = Linked("ACC-2", Monday.AddDays(1), device: "D-77");
        var three = Linked("ACC-3", Monday.AddDays(2), device: "D-77");
        var four = Linked("ACC-4", Monday.AddDays(3), device: "D-77");
        await ProjectAsync(one, two, three, four);

        // ACC-1 was on D-77 first, so the third link triggers its hit.
        var first = Assert.Single(await RingHitsAsync("ACC-1"));
        Assert.Equal(three.OccurredAt, first.TriggeredAt);
        Assert.Equal([one.EventId, two.EventId, three.EventId], first.EvidenceEventIds);
        Assert.Equal("Device D-77 is shared by 4 Accounts; Ring threshold 3", first.Explanation);

        // ACC-4 joined a group already at the threshold, so its own link triggers it.
        var fourth = Assert.Single(await RingHitsAsync("ACC-4"));
        Assert.Equal(four.OccurredAt, fourth.TriggeredAt);
        Assert.Equal([one.EventId, two.EventId, four.EventId], fourth.EvidenceEventIds);
    }

    [Fact]
    public async Task The_Ring_threshold_comes_from_the_rules_file()
    {
        await ProjectAsync(Linked("ACC-1", Monday, device: "D-77"), Linked("ACC-2", Monday.AddDays(1), device: "D-77"));
        Assert.Single(await RingHitsAsync("ACC-2", WithRingThreshold(2)));
    }

    [Fact]
    public void A_graph_rule_with_no_query_behind_it_is_rejected()
    {
        var unknown = V2 with { Rules = [.. V2.Rules, new Rule("G9", "Unknown", RuleKind.Graph, 10, "x")] };

        Assert.Throws<InvalidDataException>(() => GraphRules.EnsureKnown(unknown));
    }

    [Fact]
    public void A_Ring_threshold_below_two_is_rejected()
    {
        Assert.Throws<InvalidDataException>(() => GraphRules.EnsureKnown(WithRingThreshold(1)));
    }
}
