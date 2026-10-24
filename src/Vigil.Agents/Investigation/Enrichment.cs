using System.Text;
using Vigil.Domain.Detection;

namespace Vigil.Agents;

/// <summary>
/// The Enrich step: what the Investigator is told up front, before it calls any tool. The Alert's Score, Risk Band and
/// Rule Hits, each citing the Evidence Refs of the events behind it, all masked. Everything else comes through tools.
/// </summary>
public sealed class Enrichment
{
    private Enrichment(Alert alert, MaskedEvidence evidence, string brief, DateTimeOffset evidenceFrom)
    {
        Alert = alert;
        Evidence = evidence;
        Brief = brief;
        EvidenceFrom = evidenceFrom;
    }

    public Alert Alert { get; }
    public string AccountId => Alert.AccountId;

    /// <summary>This Investigation's masker and refs, which the tools go on adding to.</summary>
    public MaskedEvidence Evidence { get; }

    /// <summary>The masked opening message to the Investigator.</summary>
    public string Brief { get; }

    /// <summary>When the earliest event any Rule Hit cites happened: the Holder baseline is taken from before it.</summary>
    public DateTimeOffset EvidenceFrom { get; }

    public static async Task<Enrichment> CreateAsync(Alert alert, IInvestigationSource source, CancellationToken ct)
    {
        var evidence = new MaskedEvidence();
        evidence.Masker.Register(IdentifierKind.AccountId, alert.AccountId);

        var events = await source.GetEventsAsync([.. alert.Hits.SelectMany(h => h.EvidenceEventIds).Distinct()], ct);
        var ordered = events.OrderBy(e => e.OccurredAt).ThenBy(e => e.EventId).ToList();

        // Described first: that registers every identifier on the evidence, so the explanations below, which are
        // rendered from those same events, have nothing left unmasked.
        var eventLines = ordered.Select(evidence.Describe).ToList();

        var brief = new StringBuilder()
            .AppendLine($"Investigate the Case on Account {evidence.Mask(alert.AccountId)}.")
            .AppendLine($"Alert: Score {alert.Score}, Risk Band {alert.Band}, Rules Version {alert.RulesVersion}.")
            .AppendLine()
            .AppendLine("Rule Hits:");
        foreach (var hit in alert.Hits)
        {
            var refs = string.Join(", ", hit.EvidenceEventIds.Select(evidence.Refs.AddEvent));
            brief.AppendLine($"- {hit.RuleId} (+{hit.Points}) at {hit.TriggeredAt.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}: " +
                             $"{evidence.Mask(hit.Explanation)}. Evidence: {refs}");
        }
        brief.AppendLine().AppendLine("Evidence events:");
        eventLines.ForEach(line => brief.AppendLine(line));

        var own = ordered.Where(e => e.AccountId == alert.AccountId).ToList();
        var evidenceFrom = own.Count > 0 ? own[0].OccurredAt : alert.Hits.Min(h => h.TriggeredAt);
        return new Enrichment(alert, evidence, brief.ToString(), evidenceFrom);
    }
}
