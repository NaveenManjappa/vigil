using Vigil.Domain.Events;
using Vigil.Domain.Fraud;
using Vigil.Domain.Graph;

namespace Vigil.Graph.Tests;

public sealed class GraphNeighbourhoodTests(Neo4jFixture neo4j) : IClassFixture<Neo4jFixture>, IAsyncLifetime
{
    private static readonly DateTimeOffset Monday = new(2026, 9, 21, 9, 0, 0, TimeSpan.Zero);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly GraphProjector _projector = new(neo4j.Driver);
    private readonly GraphNeighbourhood _neighbourhood = new(neo4j.Driver);

    public async ValueTask InitializeAsync() =>
        await neo4j.Driver.ExecutableQuery("MATCH (n) DETACH DELETE n").ExecuteAsync(Ct);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static PortalEvent Event(
        string account, DateTimeOffset at, string? device = null, string ip = "198.51.100.1",
        Dictionary<string, string>? attributes = null) =>
        new(Guid.NewGuid(), account, $"HOLDER-{account}", EventType.LoginSucceeded, at, device, ip, "GB", "web",
            attributes ?? []);

    private Task ProjectAsync(params PortalEvent[] events) => _projector.ProjectAsync(events, Ct);

    [Fact]
    public async Task Each_Linking_Identifier_lists_the_other_Accounts_on_it_and_whether_they_are_Confirmed_Fraud()
    {
        var mine = Event("ACC-MARGARET", Monday.AddDays(2), device: "D-77");
        var derek = Event("ACC-DEREK", Monday, device: "D-77");
        var priya = Event("ACC-PRIYA", Monday.AddDays(1), device: "D-77");
        await ProjectAsync(mine, derek, priya);
        await _projector.ProjectConfirmedFraudAsync(new ConfirmedFraud("ACC-PRIYA", Monday, ConfirmedFraudSource.Seed), Ct);

        var neighbourhood = await _neighbourhood.ForAccountAsync("ACC-MARGARET", Ct);

        var device = Assert.Single(neighbourhood.Identifiers);
        Assert.Equal("Device", device.Kind);
        Assert.Equal("D-77", device.Value);
        Assert.Equal(mine.EventId, device.LinkEventId);
        Assert.Equal(
            [new LinkedAccount("ACC-DEREK", IsConfirmedFraud: false, derek.EventId), new LinkedAccount("ACC-PRIYA", IsConfirmedFraud: true, priya.EventId)],
            device.OtherAccounts);
    }

    [Fact]
    public async Task IP_addresses_and_Addresses_are_left_out_because_they_never_link_Accounts()
    {
        await ProjectAsync(
            Event("ACC-PATEL", Monday, ip: "203.0.113.9", attributes: new() { ["NewPostcode"] = "SW1A 1AA" }),
            Event("ACC-NEIGHBOUR", Monday, ip: "203.0.113.9", attributes: new() { ["NewPostcode"] = "SW1A 1AA" }));

        var neighbourhood = await _neighbourhood.ForAccountAsync("ACC-PATEL", Ct);

        Assert.Empty(neighbourhood.Identifiers);
    }

    [Fact]
    public async Task An_identifier_no_other_Account_uses_is_listed_with_no_other_Accounts()
    {
        // Margaret's own laptop and her new bank account are unshared; that is evidence too.
        var laptop = Event("ACC-MARGARET", Monday, device: "D-LAPTOP");
        var bank = Event("ACC-MARGARET", Monday.AddDays(1), attributes: new() { ["NewBankAccountHash"] = "BA-9f31" });
        var email = Event("ACC-MARGARET", Monday.AddDays(1), attributes: new() { ["NewEmail"] = "m.attacker@example.net" });
        await ProjectAsync(laptop, bank, email);

        var neighbourhood = await _neighbourhood.ForAccountAsync("ACC-MARGARET", Ct);

        Assert.Equal(
            [("BankAccount", "BA-9f31", bank.EventId), ("Device", "D-LAPTOP", laptop.EventId), ("Email", "m.attacker@example.net", email.EventId)],
            neighbourhood.Identifiers.Select(i => (i.Kind, i.Value, i.LinkEventId)));
        Assert.All(neighbourhood.Identifiers, i => Assert.Empty(i.OtherAccounts));
    }
}
