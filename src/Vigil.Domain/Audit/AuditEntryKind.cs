namespace Vigil.Domain.Audit;

/// <summary>
/// The step of a Case's life an Audit Entry records. The first two are written when the Investigation reaches Await
/// Approval, the last two by Act. Stored as text, so the order here carries no meaning in the database.
/// </summary>
public enum AuditEntryKind
{
    InvestigationCompleted,
    ReviewCompleted,
    DecisionRecorded,
    ActionTaken,
}
