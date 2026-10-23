using Vigil.Domain.Events;
using Vigil.Domain.Graph;

namespace Vigil.Agents;

/// <summary>
/// One Investigation's masker and Evidence Ref table, and the only way evidence is written out for the LLM: each
/// method registers the identifiers it is about to show, tags the evidence with a ref, then masks the line (ADR-0008).
/// </summary>
public sealed class MaskedEvidence
{
    // The Portal Event attributes that hold identifiers. Others, such as Amount, are kept because the agents reason on them.
    private static readonly Dictionary<string, IdentifierKind> IdentifierAttributes = new()
    {
        ["NewEmail"] = IdentifierKind.Email,
        ["NewPhone"] = IdentifierKind.Phone,
        ["NewBankAccountHash"] = IdentifierKind.BankAccount,
        ["NewPostcode"] = IdentifierKind.Address,
    };

    public PseudonymMasker Masker { get; } = new();
    public EvidenceRefTable Refs { get; } = new();

    public string Mask(string text) => Masker.Mask(text);

    /// <summary>One line, e.g. <c>E-3 2026-09-22T09:04:00Z BankDetailsChanged account ACCOUNT-1 device DEVICE-2 ...</c>.</summary>
    public string Describe(PortalEvent e)
    {
        Masker.Register(IdentifierKind.AccountId, e.AccountId);
        Masker.Register(IdentifierKind.HolderId, e.HolderId);
        if (!string.IsNullOrWhiteSpace(e.DeviceId)) Masker.Register(IdentifierKind.DeviceId, e.DeviceId);
        if (!string.IsNullOrWhiteSpace(e.IpAddress)) Masker.Register(IdentifierKind.IpAddress, e.IpAddress);
        foreach (var (key, value) in e.Attributes)
            if (IdentifierAttributes.TryGetValue(key, out var kind) && !string.IsNullOrWhiteSpace(value))
                Masker.Register(kind, value);

        var attributes = string.Concat(e.Attributes.OrderBy(a => a.Key).Select(a => $" {a.Key}={a.Value}"));
        return Mask($"{Refs.AddEvent(e.EventId)} {e.OccurredAt.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ} {e.Type} account {e.AccountId} " +
                    $"holder {e.HolderId} device {e.DeviceId ?? "none"} ip {e.IpAddress ?? "none"} country {e.Country ?? "unknown"} " +
                    $"channel {e.Channel}{attributes}");
    }

    /// <summary>One graph fact, e.g. <c>G-2 Device DEVICE-1 is also used by ACCOUNT-2, ACCOUNT-3 (Confirmed Fraud)</c>.</summary>
    public string Describe(LinkedIdentifier identifier)
    {
        Masker.Register(KindOf(identifier.Kind), identifier.Value);
        foreach (var other in identifier.OtherAccounts) Masker.Register(IdentifierKind.AccountId, other.AccountId);

        // The ref stands for every link in the fact, this Account's first, so the analyst can check each one.
        var graphRef = Refs.AddGraphFact([identifier.LinkEventId, .. identifier.OtherAccounts.Select(o => o.LinkEventId)]);
        var others = identifier.OtherAccounts.Count == 0
            ? "is used by no other Account"
            : "is also used by " + string.Join(", ", identifier.OtherAccounts.Select(o => o.IsConfirmedFraud ? $"{o.AccountId} (Confirmed Fraud)" : o.AccountId));
        return Mask($"{graphRef} {identifier.Kind} {identifier.Value} {others}");
    }

    public string Describe(HolderBaseline baseline)
    {
        foreach (var device in baseline.UsualDevices) Masker.Register(IdentifierKind.DeviceId, device);

        return Mask($"Usual countries: {List(baseline.UsualCountries)}. Usual devices: {List(baseline.UsualDevices)}. " +
                    $"Logins per week: {baseline.LoginsPerWeek:0.#}.");
    }

    // An identifier kind with no Pseudonym prefix would reach the model raw, so an unknown one stops the Investigation.
    private static IdentifierKind KindOf(string graphLabel) => graphLabel switch
    {
        "Device" => IdentifierKind.DeviceId,
        "Email" => IdentifierKind.Email,
        "Phone" => IdentifierKind.Phone,
        "BankAccount" => IdentifierKind.BankAccount,
        _ => throw new InvalidOperationException($"No Pseudonym kind for graph identifier '{graphLabel}'."),
    };

    private static string List(IReadOnlyList<string> values) => values.Count == 0 ? "none" : string.Join(", ", values);
}
