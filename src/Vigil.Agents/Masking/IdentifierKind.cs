namespace Vigil.Agents;

/// <summary>
/// The identifiers that name a person or their kit, so they never reach an LLM unmasked (ADR-0008).
/// Event types, times, country, channel, amounts and Rule Ids are not here on purpose: the agents reason on them.
/// </summary>
public enum IdentifierKind
{
    AccountId,
    HolderId,
    Email,
    Phone,
    BankAccount,
    Address,
    IpAddress,
    DeviceId,
}
