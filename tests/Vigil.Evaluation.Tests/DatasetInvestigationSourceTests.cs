using Vigil.Domain.Datasets;
using Vigil.Domain.Events;
using Vigil.Domain.Graph;

namespace Vigil.Evaluation.Tests;

public sealed class DatasetInvestigationSourceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateTimeOffset Start = new(2026, 9, 1, 9, 0, 0, TimeSpan.Zero);

    private static PortalEvent Event(string account, EventType type, int days, string device, string country = "GB") =>
        new(Guid.NewGuid(), account, $"HOLDER-{account}", type, Start.AddDays(days), device, "198.51.100.1", country, "web",
            new Dictionary<string, string>());

    // Two weeks of usual logins, a takeover on day 20 the Case opens on, then a sale on day 21 that comes after it.
    private static readonly PortalEvent[] Margaret =
    [
        Event("ACC-M", EventType.LoginSucceeded, 0, "D-HOME"),
        Event("ACC-M", EventType.LoginSucceeded, 7, "D-HOME"),
        Event("ACC-M", EventType.LoginSucceeded, 14, "D-HOME"),
        Event("ACC-M", EventType.PasswordReset, 20, "D-77", "RO"),
        Event("ACC-M", EventType.SaleRequested, 21, "D-77", "RO"),
    ];

    private static readonly PortalEvent Priya = Event("ACC-P", EventType.LoginSucceeded, 10, "D-77", "RO");
    private static readonly PortalEvent LaterStranger = Event("ACC-S", EventType.LoginSucceeded, 25, "D-77", "RO");

    private static readonly LabelledAccount[] Dataset =
    [
        new("ACC-M", new Label(Persona.AtoFraudster, FraudKind.Ato), Margaret),
        new("ACC-P", new Label(Persona.FraudRing, FraudKind.Ring), [Priya]),
        new("ACC-S", new Label(Persona.QuietHolder, null), [LaterStranger]),
    ];

    private static DatasetInvestigationSource SourceAsOfTheTakeover(Func<string, CancellationToken, Task<Neighbourhood>>? graph = null) =>
        new(Dataset, asOf: Margaret[3].OccurredAt, graph);

    [Fact]
    public async Task The_timeline_stops_when_the_Case_opened_so_the_agents_never_see_what_came_after()
    {
        var timeline = await SourceAsOfTheTakeover().GetTimelineAsync("ACC-M", days: 10, Ct);

        Assert.Equal([Margaret[2].EventId, Margaret[3].EventId], timeline.Select(e => e.EventId));
    }

    [Fact]
    public async Task Events_are_found_by_id_on_any_Account()
    {
        var events = await SourceAsOfTheTakeover().GetEventsAsync([Margaret[3].EventId, Priya.EventId], Ct);

        Assert.Equal(["ACC-M", "ACC-P"], events.Select(e => e.AccountId).Order());
    }

    [Fact]
    public async Task The_Holder_baseline_is_taken_from_before_the_evidence()
    {
        var baseline = await SourceAsOfTheTakeover().GetHolderBaselineAsync("ACC-M", Margaret[3].OccurredAt, Ct);

        Assert.Equal(["D-HOME"], baseline.UsualDevices);
        Assert.Equal(["GB"], baseline.UsualCountries);
    }

    [Fact]
    public async Task Without_a_graph_the_Neighbourhood_is_empty()
    {
        var neighbourhood = await SourceAsOfTheTakeover().GetNeighbourhoodAsync("ACC-M", Ct);

        Assert.Empty(neighbourhood.Identifiers);
    }

    [Fact]
    public async Task The_Neighbourhood_leaves_out_links_that_formed_after_the_Case_opened()
    {
        // The replayed graph holds the whole Dataset, so it knows about the stranger who used D-77 on day 25.
        var replayed = new Neighbourhood("ACC-M",
        [
            new LinkedIdentifier("Device", "D-77", Margaret[3].EventId,
                [new LinkedAccount("ACC-P", false, Priya.EventId), new LinkedAccount("ACC-S", false, LaterStranger.EventId)]),
            new LinkedIdentifier("Device", "D-LATER", Margaret[4].EventId, []),
        ]);

        var neighbourhood = await SourceAsOfTheTakeover((_, _) => Task.FromResult(replayed)).GetNeighbourhoodAsync("ACC-M", Ct);

        var device = Assert.Single(neighbourhood.Identifiers);
        Assert.Equal("D-77", device.Value);
        Assert.Equal(["ACC-P"], device.OtherAccounts.Select(a => a.AccountId));
    }
}
