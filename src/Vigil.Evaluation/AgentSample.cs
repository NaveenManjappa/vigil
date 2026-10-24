using Vigil.Domain.Datasets;
using Vigil.Domain.Detection;

namespace Vigil.Evaluation;

/// <summary>An Account with the Alerts detection raised on it, which is what the Agent Sample is drawn from.</summary>
public sealed record ScoredAccount(LabelledAccount Account, IReadOnlyList<Alert> Alerts)
{
    public RiskBand PeakBand => Alerts.Select(a => a.Band).DefaultIfEmpty(RiskBand.Low).Max();
}

/// <param name="Alert">The Alert the Investigation opens on: the Account's highest-scoring one.</param>
public sealed record SampledAccount(LabelledAccount Account, Alert Alert, bool IsNearMiss);

/// <summary>
/// Draws the Agent Sample (ADR-0016): <see cref="AgentSampleSize"/> Flagged Accounts taken in turn from each Persona,
/// so a rare Persona is not crowded out by a common one, plus up to a quarter as many Near-misses, so the sample
/// holds genuine Accounts the agents could clear. The seed fixes the draw, so two runs investigate the same Accounts.
/// </summary>
public static class AgentSample
{
    public const int AgentSampleSize = 20;

    public static IReadOnlyList<SampledAccount> Draw(IEnumerable<ScoredAccount> scored, int seed, int size = AgentSampleSize)
    {
        var random = new Random(seed);
        var accounts = scored.ToList();

        var flagged = accounts
            .Where(a => a.PeakBand >= RiskBand.High)
            .GroupBy(a => a.Account.Label.Persona)
            .OrderBy(g => g.Key)
            .Select(g => Shuffled(g, random))
            .ToList();
        var takenInTurn = Enumerable.Range(0, flagged.Select(g => g.Count).DefaultIfEmpty(0).Max())
            .SelectMany(round => flagged.Where(g => round < g.Count).Select(g => g[round]))
            .Take(size);

        var nearMisses = Shuffled(accounts.Where(a => !a.Account.Label.IsFraud && a.PeakBand == RiskBand.Medium), random)
            .Take(size / 4);

        return
        [
            .. takenInTurn.Select(a => Sampled(a, isNearMiss: false)),
            .. nearMisses.Select(a => Sampled(a, isNearMiss: true)),
        ];
    }

    // Sorted first, so the draw depends only on the seed and not on the order the Dataset was read in.
    private static List<ScoredAccount> Shuffled(IEnumerable<ScoredAccount> accounts, Random random)
    {
        var sorted = accounts.OrderBy(a => a.Account.AccountId, StringComparer.Ordinal).ToArray();
        random.Shuffle(sorted);
        return [.. sorted];
    }

    private static SampledAccount Sampled(ScoredAccount scored, bool isNearMiss) =>
        new(scored.Account, scored.Alerts.MaxBy(a => a.Score)!, isNearMiss);
}
