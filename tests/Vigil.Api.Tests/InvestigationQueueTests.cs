using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Vigil.Api.Cases;
using Vigil.Domain.Cases;
using Vigil.Infrastructure;

namespace Vigil.Api.Tests;

/// <summary>How a newly opened Case reaches its one Investigation: queued after the ingest commit and run by the worker.</summary>
public sealed class InvestigationQueueTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateTimeOffset Monday = new(2026, 9, 21, 9, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_high_alerts_case_is_investigated_and_waits_for_approval()
    {
        var client = factory.CreateClient();
        await TakeOverToHighAsync(client, "ACC-INV-HIGH");

        var @case = await CaseEventuallyAsync(client, "ACC-INV-HIGH", c => c.Status == CaseStatus.AwaitingApproval);
        Assert.False(@case.NotInvestigated);
    }

    [Fact]
    public async Task An_alert_rising_to_critical_does_not_queue_its_case_again()
    {
        var queue = new RecordingCaseQueue();
        await using var host = WithQueue(factory, queue);
        var client = host.CreateClient();

        await TakeOverToHighAsync(client, "ACC-INV-CRIT");
        var @case = await CaseEventuallyAsync(client, "ACC-INV-CRIT", c => c.Status == CaseStatus.AwaitingApproval);
        (await RaiseToCriticalAsync(client, "ACC-INV-CRIT")).EnsureSuccessStatusCode();

        Assert.Equal(1, queue.Enqueued.Count(id => id == @case.Id));
    }

    [Fact]
    public async Task A_rolled_back_batch_queues_no_case()
    {
        var queue = new RecordingCaseQueue();
        await using var host = WithQueue(factory, queue);
        var client = host.CreateClient();
        await FailCommitsForRollbackAccountAsync(host);

        var response = await client.PostAsJsonAsync("/events", TakeOver(RollbackAccount), Ct);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VigilDbContext>();
        var committed = await db.Cases.Select(c => c.CaseId).ToListAsync(Ct);
        Assert.All(queue.Enqueued, id => Assert.Contains(id, committed));
    }

    // Fails the transaction at COMMIT, after the Case has been written inside it: the latest point a rollback can come.
    [Fact]
    public async Task On_startup_cases_left_open_or_investigating_are_investigated()
    {
        // As if the API crashed after the ingest commit but before the worker took the Case off the queue...
        await using (var crashedBeforeDequeue = WithQueue(factory, new DroppingCaseQueue()))
        {
            var client = crashedBeforeDequeue.CreateClient();
            await TakeOverToHighAsync(client, "ACC-INV-LOST");
            Assert.Equal(CaseStatus.Open, (await CaseForAsync(client, "ACC-INV-LOST")).Status);
        }

        // ...and as if it crashed mid-Investigation.
        await using (var crashedMidRun = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
                         s.Replace(ServiceDescriptor.Scoped<IInvestigationRunner, FailingInvestigationRunner>()))))
        {
            var client = crashedMidRun.CreateClient();
            await TakeOverToHighAsync(client, "ACC-INV-INTERRUPTED");
            await CaseEventuallyAsync(client, "ACC-INV-INTERRUPTED", c => c.Status == CaseStatus.Investigating);
        }

        await using var restarted = factory.WithWebHostBuilder(_ => { });
        var restartedClient = restarted.CreateClient();

        await CaseEventuallyAsync(restartedClient, "ACC-INV-LOST", c => c.Status == CaseStatus.AwaitingApproval);
        await CaseEventuallyAsync(restartedClient, "ACC-INV-INTERRUPTED", c => c.Status == CaseStatus.AwaitingApproval);
    }

    private const string RollbackAccount = "ACC-INV-ROLLBACK";

    private static async Task FailCommitsForRollbackAccountAsync(WebApplicationFactory<Program> host)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VigilDbContext>();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE OR REPLACE FUNCTION fail_commit() RETURNS trigger LANGUAGE plpgsql AS
                $$ BEGIN RAISE EXCEPTION 'commit refused for test'; END $$;
            CREATE CONSTRAINT TRIGGER fail_commit_rollback_account AFTER INSERT ON alerts
                DEFERRABLE INITIALLY DEFERRED FOR EACH ROW
                WHEN (NEW."AccountId" = 'ACC-INV-ROLLBACK') EXECUTE FUNCTION fail_commit();
            """, Ct);
    }

    internal static WebApplicationFactory<Program> WithQueue(ApiFactory factory, ICaseQueue queue) =>
        factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.Replace(ServiceDescriptor.Singleton(queue))));

    internal static object Event(string accountId, string type, DateTimeOffset at) => new
    {
        eventId = Guid.NewGuid(),
        accountId,
        holderId = $"HOLDER-{accountId}",
        type,
        occurredAt = at,
        deviceId = $"D-{accountId}",
        ipAddress = "198.51.100.78",
        country = "RO",
        channel = "web",
    };

    // R1 + R2 + R3 = 80, High.
    internal static object[] TakeOver(string accountId) =>
    [
        Event(accountId, "PasswordReset", Monday),
        Event(accountId, "MfaEnrolled", Monday.AddMinutes(4)),
        Event(accountId, "EmailChanged", Monday.AddMinutes(10)),
        Event(accountId, "BankDetailsChanged", Monday.AddDays(1)),
    ];

    internal static async Task TakeOverToHighAsync(HttpClient client, string accountId) =>
        (await client.PostAsJsonAsync("/events", TakeOver(accountId), Ct)).EnsureSuccessStatusCode();

    // + R4 = 120, Critical.
    internal static Task<HttpResponseMessage> RaiseToCriticalAsync(HttpClient client, string accountId) =>
        client.PostAsJsonAsync("/events", new[] { Event(accountId, "SaleRequested", Monday.AddDays(2)) }, Ct);

    internal static async Task<CaseSummary> CaseForAsync(HttpClient client, string accountId) =>
        Assert.Single((await client.GetFromJsonAsync<List<CaseSummary>>("/cases", Json, Ct))!, c => c.AccountId == accountId);

    // The worker runs in the background, so the Case is polled until it settles rather than read once.
    internal static async Task<CaseSummary> CaseEventuallyAsync(HttpClient client, string accountId, Func<CaseSummary, bool> settled)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (true)
        {
            var @case = await CaseForAsync(client, accountId);
            if (settled(@case)) return @case;
            if (DateTime.UtcNow > deadline) Assert.Fail($"Case for {accountId} never settled; last seen {@case}.");
            await Task.Delay(100, Ct);
        }
    }
}

/// <summary>The real queue, recording every Case put on it so a test can see what ingestion queued.</summary>
public sealed class RecordingCaseQueue : ICaseQueue
{
    private readonly ChannelCaseQueue _inner = new();
    private readonly ConcurrentQueue<Guid> _enqueued = new();

    public IReadOnlyCollection<Guid> Enqueued => _enqueued;

    public ValueTask EnqueueAsync(QueuedCase item, CancellationToken ct)
    {
        _enqueued.Enqueue(item.CaseId);
        return _inner.EnqueueAsync(item, ct);
    }

    public IAsyncEnumerable<QueuedCase> DequeueAllAsync(CancellationToken ct) => _inner.DequeueAllAsync(ct);
}

/// <summary>Loses every Case put on it, as the in-memory queue does when the process dies.</summary>
public sealed class DroppingCaseQueue : ICaseQueue
{
    private readonly ChannelCaseQueue _neverWritten = new();

    public ValueTask EnqueueAsync(QueuedCase item, CancellationToken ct) => ValueTask.CompletedTask;

    public IAsyncEnumerable<QueuedCase> DequeueAllAsync(CancellationToken ct) => _neverWritten.DequeueAllAsync(ct);
}

/// <summary>Leaves the Case Investigating, as a crash mid-run would.</summary>
public sealed class FailingInvestigationRunner : IInvestigationRunner
{
    public Task RunAsync(Guid caseId, CancellationToken ct) => throw new InvalidOperationException("Simulated crash mid-Investigation.");
}
