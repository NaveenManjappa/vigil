using Vigil.Domain.Detection;

namespace Vigil.Detection;

/// <summary>
/// Groups Rule Hits into Risk Episodes, one Alert each. A hit joins the episode of the previous hit
/// if it came at most the risk period later; otherwise it opens a new episode. A Rule counts once per episode.
/// </summary>
internal static class RiskEpisodes
{
    /// <param name="stored">Episodes already persisted. Hits are only appended to them, never removed or reordered.</param>
    /// <param name="hits">Every candidate hit, in trigger-time order.</param>
    public static List<List<RuleHit>> Assemble(IEnumerable<IReadOnlyList<RuleHit>> stored, IEnumerable<RuleHit> hits, TimeSpan riskPeriod)
    {
        var episodes = stored.Select(e => e.ToList()).ToList();
        foreach (var hit in hits)
        {
            var home = EpisodeFor(hit, episodes, riskPeriod);
            if (home is null) episodes.Add([hit]);
            else if (home.All(h => h.RuleId != hit.RuleId)) home.Add(hit);
        }

        return episodes;
    }

    private static List<RuleHit>? EpisodeFor(RuleHit hit, List<List<RuleHit>> episodes, TimeSpan riskPeriod)
    {
        var placed = episodes.SelectMany(episode => episode.Select(h => (Episode: episode, h.TriggeredAt))).ToList();

        var previous = placed
            .Where(p => p.TriggeredAt <= hit.TriggeredAt && hit.TriggeredAt - p.TriggeredAt <= riskPeriod)
            .OrderByDescending(p => p.TriggeredAt)
            .Select(p => p.Episode)
            .FirstOrDefault();

        // Hits are assembled in trigger order, so a placed hit can only lie ahead of this one when a late event
        // completed a pattern after its episode was stored. It then joins the episode it would have started.
        return previous ?? placed
            .Where(p => p.TriggeredAt > hit.TriggeredAt && p.TriggeredAt - hit.TriggeredAt <= riskPeriod)
            .OrderBy(p => p.TriggeredAt)
            .Select(p => p.Episode)
            .FirstOrDefault();
    }
}
