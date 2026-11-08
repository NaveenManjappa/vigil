using Vigil.Domain.Detection;
using Vigil.Domain.Events;

namespace Vigil.Detection;

/// <summary>
/// Asks of the new event: has the Account seen its device and country before? A new device and a new country on the
/// same event are one hit, because they are one sighting of one stranger, not two separate signals.
/// </summary>
internal static class FirstSighting
{
    /// <param name="earlier">The Account's events before the new one.</param>
    public static RuleHit? Match(Rule rule, PortalEvent e, IReadOnlyList<PortalEvent> earlier)
    {
        // Everything is new on an Account's first event, so it says nothing about whether this one is unusual.
        if (earlier.Count == 0) return null;

        List<string> sightings = [];
        if (e.DeviceId is not null && earlier.All(x => x.DeviceId != e.DeviceId)) sightings.Add($"new device {e.DeviceId}");
        if (e.Country is not null && earlier.All(x => x.Country != e.Country)) sightings.Add($"new country {e.Country}");
        if (sightings.Count == 0) return null;

        var explanation = rule.Explanation.Replace("{sighting}", string.Join(", ", sightings));
        return new RuleHit(rule.Id, rule.Points, [e.EventId], e.OccurredAt, explanation);
    }
}
