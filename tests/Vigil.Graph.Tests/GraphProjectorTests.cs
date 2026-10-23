using Neo4j.Driver;
using Vigil.Domain.Events;

namespace Vigil.Graph.Tests;

public sealed class GraphProjectorTests(Neo4jFixture neo4j) : IClassFixture<Neo4jFixture>, IAsyncLifetime
{
    private static readonly DateTimeOffset Monday = new(2026, 9, 21, 9, 0, 0, TimeSpan.Zero);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly GraphProjector _projector = new(neo4j.Driver);

    // Each test starts from an empty graph, so expected relationships are exact.
    public async ValueTask InitializeAsync() =>
        await neo4j.Driver.ExecutableQuery("MATCH (n) DETACH DELETE n").ExecuteAsync(Ct);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static PortalEvent Event(
        EventType type, DateTimeOffset at, string account = "ACC-1", string? device = "D-1", string? ip = "198.51.100.1",
        Dictionary<string, string>? attributes = null) =>
        new(Guid.NewGuid(), account, $"HOLDER-{account}", type, at, device, ip, "GB", "web", attributes ?? []);

    /// <summary>Every relationship as "from TYPE to", where each end is shown as its label and key.</summary>
    private async Task<HashSet<string>> RelationshipsAsync()
    {
        var result = await neo4j.Driver.ExecutableQuery("""
            MATCH (from)-[r]->(to)
            RETURN labels(from)[0] + ' ' + coalesce(from.accountId, from.holderId) AS from,
                   type(r) AS type,
                   labels(to)[0] + ' ' + coalesce(to.accountId, to.value) AS to
            """).ExecuteAsync(Ct);
        return result.Result.Select(r => $"{r["from"]} {r["type"]} {r["to"]}").ToHashSet();
    }

    /// <summary>Every node and every relationship with its firstSeen, so any change to the graph shows up.</summary>
    private async Task<List<string>> SnapshotAsync()
    {
        var result = await neo4j.Driver.ExecutableQuery("""
            MATCH (n)
            OPTIONAL MATCH (n)-[r]->(to)
            RETURN labels(n)[0] + ' ' + coalesce(n.accountId, n.holderId, n.value) AS node,
                   type(r) + ' ' + coalesce(to.accountId, to.value) + ' ' + toString(r.firstSeen) AS relationship
            """).ExecuteAsync(Ct);
        return result.Result.Select(r => $"{r["node"]} {r["relationship"]}").Order().ToList();
    }

    private async Task<DateTimeOffset> FirstSeenAsync(string relationship, string value)
    {
        var result = await neo4j.Driver.ExecutableQuery($"MATCH (:Account)-[r:{relationship}]->({{value: $value}}) RETURN r.firstSeen AS firstSeen")
            .WithParameters(new { value })
            .ExecuteAsync(Ct);
        return Assert.Single(result.Result)["firstSeen"].As<ZonedDateTime>().ToDateTimeOffset();
    }

    [Fact]
    public async Task A_login_links_the_Holder_Account_device_and_IP()
    {
        await _projector.ProjectAsync([Event(EventType.LoginSucceeded, Monday)], Ct);

        Assert.Equal(
            [
                "Holder HOLDER-ACC-1 OWNS Account ACC-1",
                "Account ACC-1 USED_DEVICE Device D-1",
                "Account ACC-1 SEEN_FROM IpAddress 198.51.100.1",
            ],
            await RelationshipsAsync());
    }

    [Fact]
    public async Task Changed_details_link_the_Account_to_the_new_email_phone_bank_account_and_address()
    {
        await _projector.ProjectAsync(
            [
                Event(EventType.EmailChanged, Monday, device: null, ip: null, attributes: new() { ["NewEmail"] = "m.attacker@example.net" }),
                Event(EventType.PhoneChanged, Monday, device: null, ip: null, attributes: new() { ["NewPhone"] = "+447700900077" }),
                Event(EventType.BankDetailsChanged, Monday, device: null, ip: null, attributes: new() { ["NewBankAccountHash"] = "BA-9f31" }),
                Event(EventType.AddressChanged, Monday, device: null, ip: null, attributes: new() { ["NewPostcode"] = "RG1 8DN" }),
            ],
            Ct);

        Assert.Equal(
            [
                "Holder HOLDER-ACC-1 OWNS Account ACC-1",
                "Account ACC-1 HAS_EMAIL Email m.attacker@example.net",
                "Account ACC-1 HAS_PHONE Phone +447700900077",
                "Account ACC-1 PAYS_TO BankAccount BA-9f31",
                "Account ACC-1 LIVES_AT Address RG1 8DN",
            ],
            await RelationshipsAsync());
    }

    [Fact]
    public async Task A_blank_identifier_is_not_projected()
    {
        // Otherwise every Account sending an empty email would share one Email node and look linked.
        await _projector.ProjectAsync(
            [Event(EventType.EmailChanged, Monday, device: "", ip: " ", attributes: new() { ["NewEmail"] = "" })],
            Ct);

        Assert.Equal(["Holder HOLDER-ACC-1 OWNS Account ACC-1"], await RelationshipsAsync());
    }

    [Fact]
    public async Task Projecting_the_same_events_twice_leaves_the_graph_unchanged()
    {
        PortalEvent[] events =
        [
            Event(EventType.LoginSucceeded, Monday),
            Event(EventType.BankDetailsChanged, Monday.AddHours(1), attributes: new() { ["NewBankAccountHash"] = "BA-9f31" }),
            Event(EventType.LoginSucceeded, Monday.AddHours(2), account: "ACC-2"),
        ];
        await _projector.ProjectAsync(events, Ct);
        var once = await SnapshotAsync();

        await _projector.ProjectAsync(events, Ct);

        Assert.Equal(once, await SnapshotAsync());
    }

    [Fact]
    public async Task Accounts_using_the_same_device_share_one_Device_node()
    {
        await _projector.ProjectAsync([Event(EventType.LoginSucceeded, Monday, account: "ACC-1", device: "D-77")], Ct);
        await _projector.ProjectAsync([Event(EventType.LoginSucceeded, Monday, account: "ACC-2", device: "D-77")], Ct);

        var relationships = await RelationshipsAsync();
        Assert.Contains("Account ACC-1 USED_DEVICE Device D-77", relationships);
        Assert.Contains("Account ACC-2 USED_DEVICE Device D-77", relationships);
    }

    [Fact]
    public async Task A_late_event_moves_firstSeen_back_to_when_the_identifier_was_first_used()
    {
        await _projector.ProjectAsync([Event(EventType.LoginSucceeded, Monday.AddDays(1), device: "D-77")], Ct);
        await _projector.ProjectAsync([Event(EventType.LoginSucceeded, Monday, device: "D-77")], Ct);
        await _projector.ProjectAsync([Event(EventType.LoginSucceeded, Monday.AddDays(2), device: "D-77")], Ct);

        Assert.Equal(Monday, await FirstSeenAsync("USED_DEVICE", "D-77"));
    }

    [Theory]
    [InlineData("Account", "accountId")]
    [InlineData("Holder", "holderId")]
    [InlineData("Device", "value")]
    [InlineData("BankAccount", "value")]
    public async Task Constraints_stop_a_second_node_with_the_same_key(string label, string key)
    {
        // Run twice, as every API start does.
        await _projector.EnsureConstraintsAsync(Ct);
        await _projector.EnsureConstraintsAsync(Ct);

        var create = neo4j.Driver.ExecutableQuery($"CREATE (:{label} {{{key}: 'K-1'}})");
        await create.ExecuteAsync(Ct);

        await Assert.ThrowsAsync<ClientException>(() => create.ExecuteAsync(Ct));
    }
}
