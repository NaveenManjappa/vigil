using Vigil.Domain.Detection;
using Vigil.Domain.Events;

namespace Vigil.Detection;

/// <summary>
/// Asks of one event: does it complete this A → B rule? The event is B; A is the latest
/// earlier event matching step A, and the hit counts only if A is inside the rule's Window.
/// </summary>
internal static class SequenceMatcher
{
    /// <param name="timeline">The Account's events, already in OccurredAt order.</param>
    /// <param name="index">Position of the event being evaluated as step B.</param>
    public static RuleHit? Match(Rule rule, IReadOnlyList<PortalEvent> timeline, int index)
    {
        var b = timeline[index];
        if (!rule.Steps[1].Contains(b.Type)) return null;

        var a = timeline.Take(index).LastOrDefault(e => rule.Steps[0].Contains(e.Type));
        if (a is null) return null;

        var elapsed = b.OccurredAt - a.OccurredAt;
        if (elapsed > rule.Window) return null;

        var explanation = rule.Explanation
            .Replace("{elapsed}", FormatDuration(elapsed))
            .Replace("{window}", FormatDuration(rule.Window));

        return new RuleHit(rule.Id, rule.Points, [a.EventId, b.EventId], b.OccurredAt, explanation);
    }

    // Reads the way an analyst would say it: "4 min", "1h", "7d".
    private static string FormatDuration(TimeSpan t) =>
        t.TotalDays >= 1 && t.TotalDays % 1 == 0 ? $"{t.TotalDays}d"
        : t.TotalHours >= 1 && t.TotalHours % 1 == 0 ? $"{t.TotalHours}h"
        : $"{(int)t.TotalMinutes} min";
}
