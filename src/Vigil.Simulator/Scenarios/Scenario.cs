using Vigil.Domain.Events;

namespace Vigil.Simulator.Scenarios;

/// <summary>A fixed, scripted account journey with a known label, replayed on the simulated clock.</summary>
public abstract class Scenario
{
    /// <summary>All scripts are anchored here so runs are reproducible (Mon 2026-09-21 09:00 UTC).</summary>
    protected static readonly DateTimeOffset Anchor = new(2026, 9, 21, 9, 0, 0, TimeSpan.Zero);

    public abstract string Name { get; }
    public abstract IReadOnlyList<PortalEvent> Events();

    /// <summary>Stable EventId for the n-th step of this scenario.</summary>
    protected Guid IdFor(int step) => DeterministicGuid.Create($"{Name}/{step}");
}
