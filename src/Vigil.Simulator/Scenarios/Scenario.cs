using Vigil.Domain.Events;

namespace Vigil.Simulator.Scenarios;

/// <summary>A fixed, hand-scripted Account journey, posted to the API to demonstrate detection end to end.</summary>
public abstract class Scenario
{
    /// <summary>All scripts are anchored here so runs are reproducible (Mon 2026-09-21 09:00 UTC).</summary>
    protected static readonly DateTimeOffset Anchor = new(2026, 9, 21, 9, 0, 0, TimeSpan.Zero);

    public abstract string Name { get; }
    public abstract IReadOnlyList<PortalEvent> Events();

    /// <summary>Stable EventId for the n-th step of this scenario.</summary>
    protected Guid IdFor(int step) => DeterministicGuid.Create($"{Name}/{step}");

    /// <summary>
    /// A login a week for the 13 weeks (about 90 days) before the anchor, from the Holder's usual device and country,
    /// so the scripted journey is compared against a Baseline rather than being the Account's first sight of anything.
    /// </summary>
    protected IEnumerable<PortalEvent> Baseline(string accountId, string holderId, string device, string ip, string country) =>
        Enumerable.Range(1, 13).Select(week => new PortalEvent(
            DeterministicGuid.Create($"{Name}/baseline/{week}"), accountId, holderId, EventType.LoginSucceeded,
            Anchor.AddDays(-7 * (14 - week)), device, ip, country, "web", new Dictionary<string, string>()));
}
