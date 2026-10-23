using Microsoft.Extensions.Options;
using Vigil.Domain.Cases;
using Vigil.Infrastructure.Cases;

namespace Vigil.Api.Cases;

/// <summary>
/// Runs each queued Case's one Investigation, within the budget guard. One Case at a time keeps the daily limit
/// a simple count and LLM spend predictable; it assumes a single API instance (ADR-0004).
/// </summary>
public sealed class InvestigationWorker(
    ICaseQueue queue, IServiceScopeFactory scopes, IOptions<InvestigationOptions> options,
    TimeProvider clock, ILogger<InvestigationWorker> logger) : BackgroundService
{
    private IReadOnlyList<Guid> _unfinished = [];

    // Read here because hosted services start before the server takes requests, so the list cannot include a
    // Case opened by this run, which arrives on the queue instead.
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        using (var scope = scopes.CreateScope())
            _unfinished = await scope.ServiceProvider.GetRequiredService<CaseStore>().UnfinishedInvestigationsAsync(cancellationToken);

        if (_unfinished.Count > 0) logger.LogInformation("Resuming {Count} unfinished Investigations", _unfinished.Count);
        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // A crash loses the in-memory queue and can interrupt a run, but the Cases are still in Postgres.
        foreach (var caseId in _unfinished) await InvestigateSafelyAsync(caseId, stoppingToken);

        await foreach (var item in queue.DequeueAllAsync(stoppingToken))
            await InvestigateSafelyAsync(item.CaseId, stoppingToken);
    }

    private async Task InvestigateSafelyAsync(Guid caseId, CancellationToken ct)
    {
        try
        {
            await InvestigateAsync(caseId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The Case stays Investigating, so the next startup tries it again; one bad Case must not stop the rest.
            logger.LogError(ex, "Investigation of Case {CaseId} failed", caseId);
        }
    }

    private async Task InvestigateAsync(Guid caseId, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var cases = scope.ServiceProvider.GetRequiredService<CaseStore>();

        var @case = await cases.FindAsync(caseId, ct);
        if (@case is null || @case.Status is not (CaseStatus.Open or CaseStatus.Investigating)) return;

        // An Investigating Case was interrupted mid-run and has already counted against its day's limit.
        if (@case.Status == CaseStatus.Open)
        {
            var now = clock.GetUtcNow();
            if (!await WithinBudgetAsync(cases, now, ct))
            {
                @case.NotInvestigated = true;
                await cases.SaveAsync(ct);
                logger.LogInformation("Case {CaseId} left Open and not investigated by the budget guard", caseId);
                return;
            }

            @case.Status = CaseStatus.Investigating;
            @case.InvestigationStartedAt = now;
            await cases.SaveAsync(ct);
        }

        await scope.ServiceProvider.GetRequiredService<IInvestigationRunner>().RunAsync(caseId, ct);
    }

    private async Task<bool> WithinBudgetAsync(CaseStore cases, DateTimeOffset now, CancellationToken ct)
    {
        var limits = options.Value;
        if (!limits.Enabled) return false;

        var startOfUtcDay = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        return await cases.InvestigationsStartedSinceAsync(startOfUtcDay, ct) < limits.DailyLimit;
    }
}
