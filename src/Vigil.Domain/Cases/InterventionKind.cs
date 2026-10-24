namespace Vigil.Domain.Cases;

/// <summary>What the portal is told to do for an Analyst Decision's action. NoAction records no Intervention.</summary>
public enum InterventionKind
{
    /// <summary>Refuse bank-detail changes on the Account until the Intervention's until-date.</summary>
    BlockBankChanges,

    /// <summary>Contact the Holder by post.</summary>
    ContactHolder,
}
