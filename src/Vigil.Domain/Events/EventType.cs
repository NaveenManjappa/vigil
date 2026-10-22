namespace Vigil.Domain.Events;

/// <summary>
/// Every action the investor portal reports. Grouped by the source system that emits it,
/// so a new source can be onboarded by adding its types here and mapping them at ingestion.
/// </summary>
public enum EventType
{
    // Authentication
    LoginSucceeded,
    LoginFailed,

    // Account recovery
    PasswordResetRequested,
    PasswordReset,

    // MFA
    MfaEnrolled,
    MfaRemoved,

    // Account maintenance
    EmailChanged,
    PhoneChanged,
    AddressChanged,

    // Registration
    AccountRegistered,

    // Payment instructions
    BankDetailsChanged,

    // Transactions
    SaleRequested,
    TransferRequested,
}
