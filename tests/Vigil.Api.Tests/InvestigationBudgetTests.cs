using Microsoft.AspNetCore.Hosting;
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
}
