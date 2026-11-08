using Vigil.Domain.Detection;
using Vigil.Domain.Events;

namespace Vigil.Graph.Tests;

public sealed class GraphRulesTests(Neo4jFixture neo4j) : IClassFixture<Neo4jFixture>, IAsyncLifetime
{
    private static readonly DateTimeOffset Monday = new(2026, 9, 21, 9, 0, 0, TimeSpan.Zero);
    private static readonly RuleSet Rules = RuleSet.Load(Path.Combine(AppContext.BaseDirectory, "config", "rules.json"));
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly GraphProjector _projector = new(neo4j.Driver);
    private readonly GraphRules _rules = new(neo4j.Driver);
    private readonly GraphNeighbourhood _neighbourhood = new(neo4j.Driver);

    // Each test starts from an empty graph, so only its own links can produce a hit.
    public async ValueTask InitializeAsync() => await _projector.DeleteAllAsync(Ct);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static PortalEvent Event(string account, EventType type, string device, Dictionary<string, string>? attributes = null, string? holder = null) =>
        new(Guid.NewGuid(), account, holder ?? $"HOLDER-{account}", type, Monday, device, "198.51.100.1", "GB", "Web", attributes ?? []);

    private static PortalEvent BankChange(string account, string bankAccount, string? holder = null) =>
        Event(account, EventType.BankDetailsChanged, $"D-{account}", new() { ["NewBankAccountHash"] = bankAccount }, holder);

    // Projected first, as ingestion does, so the event's own links are in the graph when the rules run.
    private async Task<IReadOnlyList<RuleHit>> ProjectAndCheckAsync(PortalEvent e)
    {
        await _projector.ProjectAsync(e, Ct);
        return await _rules.HitsForAsync(e, Rules, Ct);
    }

    [Fact]
    public async Task A_bank_account_another_Holder_already_pays_out_to_is_G1()
    {
        await ProjectAndCheckAsync(BankChange("ACC-1", "BA-9f31"));
        var second = BankChange("ACC-2", "BA-9f31");

        var hit = Assert.Single(await ProjectAndCheckAsync(second));

        Assert.Equal("G1", hit.RuleId);
        Assert.Equal(30, hit.Points);
        Assert.Equal([second.EventId], hit.EvidenceEventIds);
        Assert.Equal("Pays out to bank account BA-9f31, which ACC-1 of a different Holder also pays out to", hit.Explanation);
    }

    [Fact]
    public async Task A_Holder_paying_two_of_their_own_Accounts_to_one_bank_account_is_not_G1()
    {
        await ProjectAndCheckAsync(BankChange("ACC-1", "BA-9f31", holder: "HOLDER-1"));

        Assert.Empty(await ProjectAndCheckAsync(BankChange("ACC-2", "BA-9f31", holder: "HOLDER-1")));
    }

    [Fact]
    public async Task A_device_used_by_a_Confirmed_Fraud_Account_is_G2()
    {
        await _projector.ProjectAsync(Event("ACC-DEREK", EventType.LoginSucceeded, "D-77"), Ct);
        await _projector.MarkConfirmedFraudAsync("ACC-DEREK", Ct);
        var reset = Event("ACC-MARGARET", EventType.PasswordReset, "D-77");

        var hit = Assert.Single(await ProjectAndCheckAsync(reset));

        Assert.Equal("G2", hit.RuleId);
        Assert.Equal(25, hit.Points);
        Assert.Equal([reset.EventId], hit.EvidenceEventIds);
        Assert.Equal("Shares Device D-77 with Confirmed Fraud Account ACC-DEREK", hit.Explanation);
    }

    [Fact]
    public async Task A_device_shared_only_with_Accounts_not_known_as_fraud_is_not_G2()
    {
        await _projector.ProjectAsync(Event("ACC-DEREK", EventType.LoginSucceeded, "D-77"), Ct);

        Assert.Empty(await ProjectAndCheckAsync(Event("ACC-MARGARET", EventType.PasswordReset, "D-77")));
    }

    [Fact]
    public async Task An_IP_address_shared_with_a_Confirmed_Fraud_Account_links_nothing()
    {
        // Both events come from 198.51.100.1, but IP addresses are not Linking Identifiers.
        await _projector.ProjectAsync(Event("ACC-DEREK", EventType.LoginSucceeded, "D-77"), Ct);
        await _projector.MarkConfirmedFraudAsync("ACC-DEREK", Ct);

        Assert.Empty(await ProjectAndCheckAsync(Event("ACC-MARGARET", EventType.LoginSucceeded, "D-MARGARET")));
    }

    [Fact]
    public async Task The_neighbourhood_lists_each_identifier_with_the_other_Accounts_on_it()
    {
        await _projector.ProjectAsync(Event("ACC-DEREK", EventType.LoginSucceeded, "D-77"), Ct);
        await _projector.MarkConfirmedFraudAsync("ACC-DEREK", Ct);
        await _projector.ProjectAsync(Event("ACC-MARGARET", EventType.LoginSucceeded, "D-MARGARET"), Ct);
        await _projector.ProjectAsync(Event("ACC-MARGARET", EventType.PasswordReset, "D-77"), Ct);

        var neighbourhood = await _neighbourhood.ForAccountAsync("ACC-MARGARET", Ct);

        Assert.False(neighbourhood.IsConfirmedFraud);
        Assert.Collection(neighbourhood.Identifiers,
            shared =>
            {
                Assert.Equal(("Device", "D-77"), (shared.Kind, shared.Value));
                var derek = Assert.Single(shared.OtherAccounts);
                Assert.Equal(("ACC-DEREK", true), (derek.AccountId, derek.IsConfirmedFraud));
            },
            own =>
            {
                Assert.Equal(("Device", "D-MARGARET"), (own.Kind, own.Value));
                Assert.Empty(own.OtherAccounts);
            });
        Assert.True((await _neighbourhood.ForAccountAsync("ACC-DEREK", Ct)).IsConfirmedFraud);
    }
}
