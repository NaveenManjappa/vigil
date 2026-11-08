using System.ComponentModel;
using Microsoft.Extensions.AI;
using Vigil.Domain.Events;

namespace Vigil.Agents;

/// <summary>
/// The three tools the Investigator may call. Each one is a plain C# method: its [Description] is what the model reads
/// to decide when to call it, and its return value is the text the model gets back. The tools are bound to one
/// Account and take no account argument, so the model cannot ask about another Holder.
/// </summary>
internal sealed class InvestigatorTools(string accountId, DateTimeOffset alertStartedAt, IAccountData data)
{
    public IList<AITool> All() =>
    [
        AIFunctionFactory.Create(GetTimeline),
        AIFunctionFactory.Create(GetLinkedAccounts),
        AIFunctionFactory.Create(GetUsualBehaviour),
    ];

    [Description("The Account's events in the last `days` days before its latest event, oldest first, one per line, each starting with its number.")]
    public async Task<string> GetTimeline([Description("How many days back to look, from 1 to 90.")] int days, CancellationToken ct)
    {
        var timeline = await data.GetTimelineAsync(accountId, ct);
        if (timeline.Count == 0) return "The Account has no events.";

        var from = timeline[^1].OccurredAt.AddDays(-Math.Clamp(days, 1, 90));
        var lines = timeline
            .Select((e, i) => (Number: i + 1, Event: e))
            .Where(x => x.Event.OccurredAt >= from)
            .Select(x => Describe(x.Number, x.Event));
        return string.Join('\n', lines);
    }

    [Description("Each device, email, phone and bank account the Account uses, and the other Accounts that use it too, marking Confirmed Fraud ones.")]
    public async Task<string> GetLinkedAccounts(CancellationToken ct)
    {
        var neighbourhood = await data.GetLinkedAccountsAsync(accountId, ct);
        if (neighbourhood.Identifiers.Count == 0) return "The Account has no devices, emails, phones or bank accounts in the graph.";

        return string.Join('\n', neighbourhood.Identifiers.Select(identifier =>
        {
            var others = identifier.OtherAccounts.Select(o => o.IsConfirmedFraud ? $"{o.AccountId} (Confirmed Fraud)" : o.AccountId).ToList();
            return others.Count == 0
                ? $"{identifier.Kind} {identifier.Value} is used by no other Account"
                : $"{identifier.Kind} {identifier.Value} is also used by {string.Join(", ", others)}";
        }));
    }

    [Description("The Holder's usual countries and devices, and how many events they had, from before the Alert started.")]
    public async Task<string> GetUsualBehaviour(CancellationToken ct)
    {
        var usual = (await data.GetTimelineAsync(accountId, ct)).Where(e => e.OccurredAt < alertStartedAt).ToList();
        if (usual.Count == 0) return "No events before the Alert started, so there is no usual behaviour to compare with.";

        var countries = usual.Select(e => e.Country).OfType<string>().Distinct();
        var devices = usual.Select(e => e.DeviceId).OfType<string>().Distinct();
        return $"Before {alertStartedAt:yyyy-MM-dd HH:mm}: {usual.Count} events. " +
               $"Usual countries: {string.Join(", ", countries)}. Usual devices: {string.Join(", ", devices)}.";
    }

    /// <summary>One event as the model reads it, e.g. <c>#3 2026-09-22 09:04 PasswordReset device D-77 country RO ...</c>.</summary>
    public static string Describe(int number, PortalEvent e)
    {
        var attributes = string.Concat(e.Attributes.OrderBy(a => a.Key).Select(a => $" {a.Key}={a.Value}"));
        return $"#{number} {e.OccurredAt.UtcDateTime:yyyy-MM-dd HH:mm} {e.Type} device {e.DeviceId ?? "none"} " +
               $"ip {e.IpAddress ?? "none"} country {e.Country ?? "unknown"} channel {e.Channel}{attributes}";
    }
}
