namespace Vigil.Domain.Detection;

public enum RiskBand
{
    Low,
    Medium,
    High,
    Critical,
}

/// <summary>One Rule matching on an Account, with the events that made it match and a plain-English explanation.</summary>
/// <param name="TriggeredAt">When the event that completed the pattern happened.</param>
public sealed record RuleHit(
    string RuleId,
    int Points,
    IReadOnlyList<Guid> EvidenceEventIds,
    DateTimeOffset TriggeredAt,
    string Explanation);

/// <summary>
/// Detection's running verdict on one Account. An Account has at most one open Alert. Each Rule counts once in it, so
/// the Score is the sum of the distinct Rules hit and can only grow.
/// </summary>
public sealed class Alert
{
    // For Entity Framework.
    private Alert() { }

    public Alert(string accountId, string rulesVersion)
    {
        Id = Guid.NewGuid();
        AccountId = accountId;
        RulesVersion = rulesVersion;
    }

    public Guid Id { get; private set; }
    public string AccountId { get; private set; } = "";
    public string RulesVersion { get; private set; } = "";
    public List<RuleHit> Hits { get; private set; } = [];
    public int Score { get; private set; }
    public RiskBand Band { get; private set; }

    /// <summary>False once its Case is closed or it went quiet for longer than the risk period.</summary>
    public bool IsOpen { get; set; } = true;

    /// <summary>Adds the hits whose Rule is not on the Alert yet, and returns the ones it added.</summary>
    public IReadOnlyList<RuleHit> Add(IEnumerable<RuleHit> hits, BandThresholds bands)
    {
        var added = hits.Where(hit => Hits.All(h => h.RuleId != hit.RuleId)).DistinctBy(hit => hit.RuleId).ToList();
        Hits.AddRange(added);
        Score = Hits.Sum(h => h.Points);
        Band = bands.BandFor(Score);
        return added;
    }

    /// <summary>
    /// True when nothing has hit for longer than the risk period before <paramref name="at"/>. Measured in event time,
    /// so replaying old events gives the same Alerts as live ones.
    /// </summary>
    public bool HasGoneQuiet(DateTimeOffset at, int riskPeriodDays) =>
        Hits.Count > 0 && at - Hits.Max(h => h.TriggeredAt) > TimeSpan.FromDays(riskPeriodDays);
}
