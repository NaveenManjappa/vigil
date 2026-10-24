using Vigil.Domain.Datasets;
using Vigil.Domain.Detection;

namespace Vigil.Evaluation.Tests;

public sealed class AgentSampleTests
{
    private static Alert AlertOf(string account, int score, RiskBand band) => new(account, score, band, "v1", []);

    private static ScoredAccount Scored(string id, Persona persona, FraudKind? kind, params Alert[] alerts) =>
        new(new LabelledAccount(id, new Label(persona, kind), []), alerts);

    private static IEnumerable<ScoredAccount> Flagged(Persona persona, FraudKind? kind, int count) =>
        Enumerable.Range(1, count).Select(i => Scored($"{persona}-{i}", persona, kind, AlertOf($"{persona}-{i}", 60, RiskBand.High)));

    private static IEnumerable<ScoredAccount> NearMisses(int count) =>
        Enumerable.Range(1, count).Select(i => Scored($"NEAR-{i}", Persona.NewPhone, null, AlertOf($"NEAR-{i}", 40, RiskBand.Medium)));

    private static readonly ScoredAccount[] NeverSampled =
    [
        Scored("QUIET-1", Persona.QuietHolder, null),
        Scored("QUIET-2", Persona.QuietHolder, null, AlertOf("QUIET-2", 10, RiskBand.Low)),
        // Fraud that stopped at Medium is a miss, not a Near-miss: Near-misses test clearing a genuine Holder.
        Scored("SLOW-1", Persona.SlowBurnFraudster, FraudKind.Evasion, AlertOf("SLOW-1", 40, RiskBand.Medium)),
    ];

    [Fact]
    public void The_Flagged_Accounts_are_spread_across_Personas_rather_than_taken_from_the_commonest()
    {
        var scored = Flagged(Persona.AtoFraudster, FraudKind.Ato, 10)
            .Concat(Flagged(Persona.RegistrationFraudster, FraudKind.Registration, 10))
            .Concat(Flagged(Persona.NewPhone, null, 2));

        var sample = AgentSample.Draw(scored, seed: 42, size: 6);

        Assert.Equal(6, sample.Count);
        Assert.All(sample, s => Assert.False(s.IsNearMiss));
        Assert.Equal(
            new Dictionary<Persona, int> { [Persona.NewPhone] = 2, [Persona.AtoFraudster] = 2, [Persona.RegistrationFraudster] = 2 },
            sample.CountBy(s => s.Account.Label.Persona).ToDictionary());
    }

    [Fact]
    public void Up_to_a_quarter_of_the_sample_size_is_added_as_Near_misses_and_nothing_below_Medium_or_fraud_is()
    {
        var scored = Flagged(Persona.AtoFraudster, FraudKind.Ato, 10).Concat(NearMisses(5)).Concat(NeverSampled);

        var sample = AgentSample.Draw(scored, seed: 42, size: 8);

        Assert.Equal(10, sample.Count);
        Assert.Equal(2, sample.Count(s => s.IsNearMiss));
        Assert.All(sample.Where(s => s.IsNearMiss), s => Assert.StartsWith("NEAR-", s.Account.AccountId));
    }

    [Fact]
    public void With_few_Flagged_Accounts_the_Near_misses_stay_at_most_a_quarter_of_the_sample()
    {
        var scored = Flagged(Persona.AtoFraudster, FraudKind.Ato, 8).Concat(NearMisses(10));

        var sample = AgentSample.Draw(scored, seed: 42, size: 20);

        Assert.Equal(10, sample.Count);
        Assert.Equal(2, sample.Count(s => s.IsNearMiss));
    }

    [Fact]
    public void Fewer_candidates_than_the_sample_size_are_all_taken()
    {
        var scored = Flagged(Persona.AtoFraudster, FraudKind.Ato, 3).Concat(NearMisses(1)).Concat(NeverSampled);

        var sample = AgentSample.Draw(scored, seed: 42, size: 20);

        Assert.Equal(4, sample.Count);
        Assert.Single(sample, s => s.IsNearMiss);
    }

    [Fact]
    public void The_same_seed_draws_the_same_sample_and_another_seed_a_different_one()
    {
        var scored = Flagged(Persona.AtoFraudster, FraudKind.Ato, 30).Concat(NearMisses(30)).ToList();

        string Ids(int seed) => string.Join(",", AgentSample.Draw(scored, seed, size: 8).Select(s => s.Account.AccountId));

        Assert.Equal(Ids(42), Ids(42));
        Assert.NotEqual(Ids(42), Ids(7));
    }

    [Fact]
    public void An_Account_is_investigated_on_its_highest_scoring_Alert()
    {
        var episodes = Scored("ATO-1", Persona.AtoFraudster, FraudKind.Ato,
            AlertOf("ATO-1", 30, RiskBand.Medium), AlertOf("ATO-1", 90, RiskBand.Critical), AlertOf("ATO-1", 60, RiskBand.High));

        var sampled = Assert.Single(AgentSample.Draw([episodes], seed: 42));

        Assert.Equal(90, sampled.Alert.Score);
    }
}
