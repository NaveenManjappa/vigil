using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Vigil.Api.Cases;
using Vigil.Domain.Cases;
using static Vigil.Api.Tests.InvestigationQueueTests;

namespace Vigil.Api.Tests;

/// <summary>
/// The budget guard. Its own containers, because the daily limit counts every Investigation in the database,
/// and each test runs its own host so it can set its own limits.
/// </summary>
public sealed class InvestigationBudgetTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task With_investigations_switched_off_a_case_stays_open_marked_not_investigated()
    {
        await using var host = factory.WithWebHostBuilder(b => b.UseSetting("Investigation:Enabled", "false"));
        var client = host.CreateClient();

        await TakeOverToHighAsync(client, "ACC-BUDGET-OFF");

        var @case = await CaseEventuallyAsync(client, "ACC-BUDGET-OFF", c => c.NotInvestigated);
        Assert.Equal(CaseStatus.Open, @case.Status);
    }

    [Fact]
    public async Task Once_the_daily_limit_is_spent_a_case_stays_open_marked_not_investigated()
    {
        await using var host = factory.WithWebHostBuilder(b => b.UseSetting("Investigation:DailyLimit", "1"));
        var client = host.CreateClient();

        await TakeOverToHighAsync(client, "ACC-BUDGET-FIRST");
        var first = await CaseEventuallyAsync(client, "ACC-BUDGET-FIRST", c => c.Status == CaseStatus.AwaitingApproval);
        await TakeOverToHighAsync(client, "ACC-BUDGET-SECOND");
        var second = await CaseEventuallyAsync(client, "ACC-BUDGET-SECOND", c => c.NotInvestigated);

        Assert.False(first.NotInvestigated);
        Assert.Equal(CaseStatus.Open, second.Status);
    }

    [Fact]
    public async Task An_investigation_left_unfinished_is_not_run_again_once_the_daily_limit_is_spent()
    {
        // The Investigation it started counts against today, so a limit of 1 is already spent whatever else ran.
        await using (var failing = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
                         s.Replace(ServiceDescriptor.Scoped<IInvestigationRunner, FailingInvestigationRunner>()))))
        {
            var client = failing.CreateClient();
            await TakeOverToHighAsync(client, "ACC-BUDGET-STUCK");
            await CaseEventuallyAsync(client, "ACC-BUDGET-STUCK", c => c.Status == CaseStatus.Investigating);
        }

        var runner = new CountingInvestigationRunner();
        await using var restarted = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Investigation:DailyLimit", "1");
            b.ConfigureTestServices(s => s.Replace(ServiceDescriptor.Singleton<IInvestigationRunner>(runner)));
        });
        var restartedClient = restarted.CreateClient();

        // The worker resumes unfinished Cases before it reads the queue, so once a new Case is turned away the stuck one has been seen.
        await TakeOverToHighAsync(restartedClient, "ACC-BUDGET-AFTER-RESTART");
        await CaseEventuallyAsync(restartedClient, "ACC-BUDGET-AFTER-RESTART", c => c.NotInvestigated);

        Assert.Equal(0, runner.Runs);
        Assert.Equal(CaseStatus.Investigating, (await CaseForAsync(restartedClient, "ACC-BUDGET-STUCK")).Status);
    }
}

public sealed class CountingInvestigationRunner : IInvestigationRunner
{
    private int _runs;

    public int Runs => _runs;

    public Task RunAsync(Guid caseId, CancellationToken ct)
    {
        Interlocked.Increment(ref _runs);
        return Task.CompletedTask;
    }
}
