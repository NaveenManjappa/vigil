using Microsoft.EntityFrameworkCore;
using Vigil.Api.Accounts;
using Vigil.Api.Data;
using Vigil.Detection;
using Vigil.Domain.Detection;
using Vigil.Domain.Events;
using Vigil.Graph;

namespace Vigil.Api.Events;

/// <summary>What one event did, so you can see the change straight from the response.</summary>
/// <param name="EventNumber">The event's position in the Account's timeline, the number the Investigator cites.</param>
/// <param name="NewHits">The Rule Hits this event added to the Alert.</param>
/// <param name="RepeatedRules">Rules this event matched that were already on the Alert, so they add no points.</param>
/// <param name="CaseOpened">The Case this event opened, if it took the Alert to the Case band.</param>
public sealed record IngestResult(
    string Message, int EventNumber, IReadOnlyList<RuleHit> NewHits, IReadOnlyList<string> RepeatedRules, Alert? Alert, Guid? CaseOpened);

/// <summary>
/// Takes in one Portal Event: stores it, copies it into the graph, runs the Rules, updates the Account's Alert and
/// opens a Case if the Alert reached the Case band. This is a single-user demo, so there is no locking between requests.
/// </summary>
public sealed class Ingestion(VigilDbContext db, AccountData accounts, GraphProjector graph, GraphRules graphRules, RuleSet rules, TimeProvider clock)
{
    public async Task<IngestResult> IngestAsync(PortalEvent e, CancellationToken ct)
    {
        var history = await accounts.GetTimelineAsync(e.AccountId, ct);
        var number = Timeline.Order([.. history.Where(x => x.EventId != e.EventId), e]).IndexOf(e) + 1;

        // The same EventId twice changes nothing, so a source can safely retry.
        if (history.Any(x => x.EventId == e.EventId))
            return new IngestResult($"Event #{number} was already ingested; nothing changed.", number, [], [], null, null);

        // 1. Store the event and copy it into the graph.
        db.Events.Add(StoredEvent.From(e));
        await graph.ProjectAsync(e, ct);

        // 2. Ask the Rules what this event triggered: the Account's own rules, then the graph rules.
        var hits = Detector.Detect(e, history, rules)
            .Concat(await graphRules.HitsForAsync(e, rules, ct))
            .ToList();

        // 3. Add the hits to the Account's open Alert. An Alert that went quiet is closed, and a new one starts.
        var alert = await db.Alerts.SingleOrDefaultAsync(a => a.AccountId == e.AccountId && a.IsOpen, ct);
        if (alert is not null && hits.Count > 0 && alert.HasGoneQuiet(e.OccurredAt, rules.RiskPeriodDays))
        {
            alert.IsOpen = false;
            alert = null;
        }
        if (alert is null && hits.Count > 0)
        {
            alert = new Alert(e.AccountId, rules.Version);
            db.Alerts.Add(alert);
        }
        var newHits = alert?.Add(hits, rules.Bands) ?? [];
        var repeated = hits.Select(h => h.RuleId).Except(newHits.Select(h => h.RuleId)).ToList();

        // 4. Open a Case the first time the Alert reaches the Case band.
        Case? opened = null;
        if (alert is not null && alert.Band >= rules.OpenCaseAt && !await db.Cases.AnyAsync(c => c.AlertId == alert.Id, ct))
        {
            opened = new Case { AccountId = e.AccountId, Alert = alert, AlertId = alert.Id, OpenedAt = clock.GetUtcNow() };
            db.Cases.Add(opened);
        }

        await db.SaveChangesAsync(ct);
        return new IngestResult(Describe(number, newHits, repeated, alert, opened), number, newHits, repeated, alert, opened?.Id);
    }

    // One line that says what changed, e.g. "Event #6 added R1 (+30). Alert: Score 65, High. Case 3f2a... opened."
    private static string Describe(int number, IReadOnlyList<RuleHit> newHits, List<string> repeated, Alert? alert, Case? opened)
    {
        var message = newHits.Count > 0
            ? $"Event #{number} added {string.Join(", ", newHits.Select(h => $"{h.RuleId} (+{h.Points})"))}."
            : $"Event #{number} added no Rule Hits.";
        if (repeated.Count > 0) message += $" {string.Join(", ", repeated)} matched again but already counts on the Alert.";
        if (alert is not null) message += $" Alert: Score {alert.Score}, {alert.Band}.";
        if (opened is not null) message += $" Case {opened.Id} opened.";
        return message;
    }
}
