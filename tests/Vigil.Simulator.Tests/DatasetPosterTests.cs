using Vigil.Domain.Datasets;
using Vigil.Domain.Events;
using Vigil.Simulator.StandIn;

namespace Vigil.Simulator.Tests;

public sealed class DatasetPosterTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 1, 9, 0, 0, TimeSpan.Zero);

    private static PortalEvent Event(string account, int hour) => new(
        Guid.NewGuid(), account, $"H-{account}", EventType.LoginSucceeded, T0.AddHours(hour), "D-1", "203.0.113.1", "GB", "web",
        new Dictionary<string, string>());

    private static LabelledAccount Account(string id, params int[] hours) =>
        new(id, new Label(Persona.QuietHolder, null), [.. hours.Select(h => Event(id, h))]);

    [Fact]
    public void Events_from_every_Account_are_posted_in_OccurredAt_order_in_batches_of_the_given_size()
    {
        var batches = DatasetPoster.Batches([Account("A", 0, 3, 5), Account("B", 1, 2, 4)], batchSize: 4);

        Assert.Equal(
            [[("A", 0), ("B", 1), ("B", 2), ("A", 3)], [("B", 4), ("A", 5)]],
            batches.Select(b => b.Select(e => (e.AccountId, (int)(e.OccurredAt - T0).TotalHours)).ToList()).ToList());
    }
}
