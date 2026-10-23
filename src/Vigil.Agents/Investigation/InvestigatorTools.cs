using System.ComponentModel;
using Microsoft.Extensions.AI;

namespace Vigil.Agents;

/// <summary>
/// The Investigator's three tools (ADR-0007). They are bound to the Case's Account and take no account argument, so
/// the model cannot ask about another Holder. Every result is masked and every new fact gets an Evidence Ref.
/// </summary>
internal sealed class InvestigatorTools(Enrichment enrichment, IInvestigationSource source)
{
    // Keeps a model that asks for "all of it" inside the token budget; three months covers any Risk Episode.
    public const int MaxTimelineDays = 90;

    public IList<AITool> AsAITools() =>
    [
        AIFunctionFactory.Create(GetAccountTimeline),
        AIFunctionFactory.Create(QueryGraphNeighbourhood),
        AIFunctionFactory.Create(GetHolderBaseline),
    ];

    [Description("The Account's events in the last `days` days up to its latest event, one per line, each with its Evidence Ref.")]
    public async Task<string> GetAccountTimeline(
        [Description("How many days back to look, from 1 to 90.")] int days, CancellationToken ct)
    {
        days = Math.Clamp(days, 1, MaxTimelineDays);
        var events = await source.GetTimelineAsync(enrichment.AccountId, days, ct);
        return events.Count == 0
            ? $"No events in the last {days} days."
            : string.Join('\n', events.Select(enrichment.Evidence.Describe));
    }

    [Description("Each of the Account's Linking Identifiers and the other Accounts that use it, marking Confirmed Fraud ones, each with its Evidence Ref.")]
    public async Task<string> QueryGraphNeighbourhood(CancellationToken ct)
    {
        var neighbourhood = await source.GetNeighbourhoodAsync(enrichment.AccountId, ct);
        return neighbourhood.Identifiers.Count == 0
            ? "The Account has no Linking Identifiers in the graph."
            : string.Join('\n', neighbourhood.Identifiers.Select(enrichment.Evidence.Describe));
    }

    [Description("The Holder's usual countries, devices and login frequency, from before the flagged activity began.")]
    public async Task<string> GetHolderBaseline(CancellationToken ct) =>
        enrichment.Evidence.Describe(await source.GetHolderBaselineAsync(enrichment.AccountId, enrichment.FlaggedFrom, ct));
}
