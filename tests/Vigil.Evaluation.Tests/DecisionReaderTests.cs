using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Vigil.Domain.Cases;
using Vigil.Domain.Detection;
using Vigil.Evaluation.Tuning;
using Vigil.Infrastructure;
using Vigil.Infrastructure.Alerts;
using Vigil.Infrastructure.Cases;

namespace Vigil.Evaluation.Tests;

/// <summary>A throwaway Postgres with the API's migrations applied, so the read runs against the real schema.</summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public string ConnectionString => _postgres.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var db = DecisionReader.Open(ConnectionString);
        await db.Database.MigrateAsync();
    }

    public async ValueTask DisposeAsync() => await _postgres.DisposeAsync();
}

public sealed class DecisionReaderTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private static readonly DateTimeOffset At = new(2026, 9, 1, 9, 0, 0, TimeSpan.Zero);

    private static StoredCase Case(string accountId, CaseStatus status, string[] ruleIds, StoredAnalystDecision? decision)
    {
        var caseId = Guid.NewGuid();
        var alert = new StoredAlert
        {
            AlertId = Guid.NewGuid(),
            AccountId = accountId,
            Score = 60,
            Band = RiskBand.High,
            RulesVersion = "v2",
            CreatedAt = At,
            UpdatedAt = At,
            Hits = [.. ruleIds.Select(id => new StoredRuleHit { Id = Guid.NewGuid(), RuleId = id, Points = 20, TriggeredAt = At, Explanation = id })],
        };
        return new StoredCase
        {
            CaseId = caseId,
            AlertId = alert.AlertId,
            Alert = alert,
            Status = status,
            OpenedAt = At,
            Decision = decision is null ? null : new StoredAnalystDecision
            {
                CaseId = caseId,
                Outcome = decision.Outcome,
                Action = decision.Action,
                AnalystId = decision.AnalystId,
                DecidedAt = At,
            },
        };
    }

    private static StoredAnalystDecision Decided(string outcome, string analyst) => new() { Outcome = outcome, Action = "NoAction", AnalystId = analyst };

    [Fact]
    public async Task Reads_each_decided_Case_with_its_outcome_analyst_and_the_Rules_its_Alert_contained()
    {
        var fraud = Case("ACC-1", CaseStatus.Closed, ["R0", "R1", "R2"], Decided("ConfirmedFraud", "stand-in"));
        var genuine = Case("ACC-2", CaseStatus.Closed, ["R2"], Decided("FalsePositive", "analyst-7"));
        var undecided = Case("ACC-3", CaseStatus.AwaitingApproval, ["R4"], null);
        await using (var db = DecisionReader.Open(fixture.ConnectionString))
        {
            db.Cases.AddRange(fraud, genuine, undecided);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var decisions = await DecisionReader.ReadAsync(fixture.ConnectionString, TestContext.Current.CancellationToken);

        var byCase = decisions.ToDictionary(d => d.CaseId);
        Assert.False(byCase.ContainsKey(undecided.CaseId));
        Assert.Equal("ConfirmedFraud", byCase[fraud.CaseId].Outcome);
        Assert.Equal("stand-in", byCase[fraud.CaseId].AnalystId);
        Assert.Equal(["R0", "R1", "R2"], byCase[fraud.CaseId].RuleIds.Order());
        Assert.Equal("FalsePositive", byCase[genuine.CaseId].Outcome);
        Assert.Equal("analyst-7", byCase[genuine.CaseId].AnalystId);
        Assert.Equal(["R2"], byCase[genuine.CaseId].RuleIds);
    }
}
