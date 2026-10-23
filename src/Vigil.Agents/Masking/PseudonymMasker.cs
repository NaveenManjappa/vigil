using System.Text.RegularExpressions;

namespace Vigil.Agents;

/// <summary>
/// Gives each identifier one Investigation sees a Pseudonym, and swaps them in and out of text (ADR-0008).
/// </summary>
public sealed class PseudonymMasker
{
    private static readonly Dictionary<IdentifierKind, string> Prefixes = new()
    {
        [IdentifierKind.AccountId] = "ACCOUNT",
        [IdentifierKind.HolderId] = "HOLDER",
        [IdentifierKind.Email] = "EMAIL",
        [IdentifierKind.Phone] = "PHONE",
        [IdentifierKind.BankAccount] = "BANK",
        [IdentifierKind.Address] = "ADDRESS",
        [IdentifierKind.IpAddress] = "IP",
        [IdentifierKind.DeviceId] = "DEVICE",
    };

    private readonly Dictionary<string, string> _pseudonymByValue = new();
    private readonly Dictionary<string, int> _countByPrefix = new();

    /// <summary>Pseudonym to real value: what is stored with the Case so the Finding can be unmasked later.</summary>
    public IReadOnlyDictionary<string, string> Map => _pseudonymByValue.ToDictionary(p => p.Value, p => p.Key);

    public static PseudonymMasker FromMap(IReadOnlyDictionary<string, string> map)
    {
        var masker = new PseudonymMasker();
        foreach (var (pseudonym, value) in map)
        {
            masker._pseudonymByValue[value] = pseudonym;

            // New values must number after the stored ones, or DEVICE-2 could come to mean two devices.
            var dash = pseudonym.LastIndexOf('-');
            var prefix = pseudonym[..dash];
            var number = int.Parse(pseudonym[(dash + 1)..]);
            masker._countByPrefix[prefix] = Math.Max(masker._countByPrefix.GetValueOrDefault(prefix), number);
        }
        return masker;
    }

    /// <summary>Returns the value's Pseudonym, assigning the next one for its kind the first time it is seen.</summary>
    public string Register(IdentifierKind kind, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (_pseudonymByValue.TryGetValue(value, out var existing))
            return existing;

        var prefix = Prefixes[kind];
        var number = _countByPrefix.GetValueOrDefault(prefix) + 1;
        _countByPrefix[prefix] = number;
        return _pseudonymByValue[value] = $"{prefix}-{number}";
    }

    public string Mask(string text) => ReplaceAll(text, _pseudonymByValue);

    public string Unmask(string text) => ReplaceAll(text, Map);

    // One regex pass with the longest keys first: at each position the longest match wins, so ACC-PRIYA
    // is never masked as ACC-PRI + "YA", and a replacement is never scanned again and re-masked.
    private static string ReplaceAll(string text, IReadOnlyDictionary<string, string> replacements)
    {
        if (replacements.Count == 0)
            return text;

        var pattern = string.Join('|', replacements.Keys.OrderByDescending(k => k.Length).Select(Regex.Escape));
        return Regex.Replace(text, pattern, match => replacements[match.Value]);
    }
}
